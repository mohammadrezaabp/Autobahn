using System.Text.RegularExpressions;

namespace Autobahn.Domain.Models;

/// <summary>
/// The rules for a model's list of backends: how the old one-url shape and the list are kept in
/// step, what a backend may be called, and the key the per-upstream state (health, the circuit
/// breaker, requests in flight, the stored credential) is filed under.
/// </summary>
public static partial class ModelBackends
{
    /// <summary>The name of the only backend of a model written in the one-url shape.</summary>
    public const string DefaultName = "default";

    public const int MaxLabels = 16;

    public const int MaxLabelKeyLength = 63;

    public const int MaxLabelValueLength = 255;

    // Never shown: a model id is free text, so any printable separator could collide with one.
    private const char KeySeparator = '\u001f';

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,62}$")]
    private static partial Regex NamePattern();

    public static bool IsValidName(string? name) =>
        !string.IsNullOrEmpty(name) && NamePattern().IsMatch(name);

    public static bool IsDefault(string? name) =>
        string.Equals(name, DefaultName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The key per-upstream state is filed under. The default backend's key is the model id itself,
    /// so a model with one backend keeps the key it always had.
    /// </summary>
    public static string Key(string modelId, string? backendName) =>
        string.IsNullOrEmpty(backendName) || IsDefault(backendName)
            ? modelId
            : string.Concat(modelId, KeySeparator.ToString(), backendName);

    public static (string ModelId, string BackendName) SplitKey(string key)
    {
        var at = key.IndexOf(KeySeparator, StringComparison.Ordinal);
        return at < 0 ? (key, DefaultName) : (key[..at], key[(at + 1)..]);
    }

    /// <summary>A key as a person reads it: the model id, with the backend's name when it has one of its own.</summary>
    public static string Display(string key)
    {
        var (modelId, backendName) = SplitKey(key);
        return Display(modelId, backendName);
    }

    public static string Display(string modelId, string? backendName) =>
        string.IsNullOrEmpty(backendName) || IsDefault(backendName) ? modelId : $"{modelId} / {backendName}";

    /// <summary>
    /// The model's backends, whichever shape it was written in. Does not change the model.
    /// </summary>
    public static IReadOnlyList<ModelBackendConfig> GetBackends(this ModelConfig model)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (model.Backends is not null)
        {
            return model.Backends;
        }

        return string.IsNullOrWhiteSpace(model.Url) ? [] : [FromTopLevel(model)];
    }

    public static ModelBackendConfig? FindBackend(this ModelConfig model, string? name) =>
        model.GetBackends().FirstOrDefault(
            b => string.Equals(b.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>True when at least one backend is in service; a model without one forwards nothing.</summary>
    public static bool HasServingBackend(this ModelConfig? model) =>
        model is not null && model.GetBackends().Any(b => b.IsServing());

    /// <summary>
    /// Brings the model to the list shape in place: an old-shape model gains its one backend, and
    /// the top-level address, credential and context length are rewritten from the list.
    /// </summary>
    public static ModelConfig Normalize(ModelConfig model)
    {
        ArgumentNullException.ThrowIfNull(model);

        model.Backends ??= string.IsNullOrWhiteSpace(model.Url) ? [] : [FromTopLevel(model)];

        foreach (var backend in model.Backends)
        {
            backend.Name = string.IsNullOrWhiteSpace(backend.Name) ? DefaultName : backend.Name.Trim();
            backend.Url = backend.Url?.Trim() ?? string.Empty;
            backend.State = ModelRouteStates.Normalize(backend.State);
            backend.Priority = BackendPriorities.Normalize(backend.Priority);
            backend.UpstreamModel = string.IsNullOrWhiteSpace(backend.UpstreamModel) ? null : backend.UpstreamModel.Trim();
            backend.Labels ??= new Dictionary<string, string>(StringComparer.Ordinal);
        }

        model.Labels ??= new Dictionary<string, string>(StringComparer.Ordinal);
        SyncTopLevel(model);
        return model;
    }

    /// <summary>
    /// Rewrites the top-level mirror from the list: the first backend's address and credential, and
    /// the largest context among the backends in service (among all of them when none is).
    /// </summary>
    public static void SyncTopLevel(ModelConfig model)
    {
        var backends = model.Backends;
        if (backends is null)
        {
            return;
        }

        if (backends.Count == 0)
        {
            model.Url = string.Empty;
            model.UpstreamAuth = null;
            return;
        }

        var first = backends[0];
        model.Url = first.Url;
        model.UpstreamAuth = CloneAuth(first.UpstreamAuth);

        var serving = backends.Where(b => b.IsServing()).ToList();
        model.MaxContextLength = (serving.Count > 0 ? serving : backends).Max(b => b.MaxContextLength);
    }

    /// <summary>
    /// The backends a whole-model update leaves the model with.
    /// </summary>
    /// <remarks>
    /// <para>A body carrying a list replaces the list, except for each backend's state: like the
    /// model's own, that moves only through stop and start, so a backend keeps the state it had and
    /// only a new one takes the state it was written with.</para>
    ///
    /// <para>A body in the one-url shape comes from a caller that does not know about lists. For a
    /// model with at most one backend it describes that backend. For a model with several it cannot,
    /// so the list is kept as it is — and a body whose address is not the one this model reports is
    /// refused rather than applied to an arbitrary backend.</para>
    /// </remarks>
    public static bool TryMergeUpdate(
        ModelConfig existing,
        ModelConfig incoming,
        out List<ModelBackendConfig> backends,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(incoming);
        error = null;

        var current = existing.GetBackends();

        if (incoming.Backends is not null)
        {
            backends = incoming.Backends.Select(Clone).ToList();
            foreach (var backend in backends)
            {
                var previous = current.FirstOrDefault(
                    b => string.Equals(b.Name, backend.Name?.Trim(), StringComparison.OrdinalIgnoreCase));
                if (previous is not null)
                {
                    backend.State = previous.State;
                }
            }

            return true;
        }

        if (current.Count > 1)
        {
            if (!string.IsNullOrWhiteSpace(incoming.Url) &&
                !string.Equals(incoming.Url.Trim(), current[0].Url?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                backends = [];
                error = $"Model '{existing.Id}' has {current.Count} backends, so a single url cannot describe it. "
                        + "Send 'backends', or change one backend through /backends/{name}.";
                return false;
            }

            backends = current.Select(Clone).ToList();
            return true;
        }

        if (string.IsNullOrWhiteSpace(incoming.Url))
        {
            backends = current.Select(Clone).ToList();
            return true;
        }

        var only = current.Count == 1 ? Clone(current[0]) : new ModelBackendConfig();
        only.Url = incoming.Url.Trim();
        only.UpstreamAuth = CloneAuth(incoming.UpstreamAuth);
        only.MaxContextLength = incoming.MaxContextLength;
        backends = [only];
        return true;
    }

    public static bool TryValidateLabels(IReadOnlyDictionary<string, string>? labels, out string? error)
    {
        error = null;
        if (labels is null || labels.Count == 0)
        {
            return true;
        }

        if (labels.Count > MaxLabels)
        {
            error = $"labels may hold at most {MaxLabels} entries.";
            return false;
        }

        foreach (var (key, value) in labels)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Length > MaxLabelKeyLength)
            {
                error = $"a label key must be 1 to {MaxLabelKeyLength} characters.";
                return false;
            }

            if (value is null || value.Length > MaxLabelValueLength)
            {
                error = $"the value of label '{key}' must be at most {MaxLabelValueLength} characters.";
                return false;
            }
        }

        return true;
    }

    public static ModelBackendConfig Clone(ModelBackendConfig backend) =>
        new()
        {
            Name = backend.Name,
            Url = backend.Url,
            UpstreamAuth = CloneAuth(backend.UpstreamAuth),
            MaxContextLength = backend.MaxContextLength,
            State = ModelRouteStates.Normalize(backend.State),
            UpstreamModel = backend.UpstreamModel,
            Priority = BackendPriorities.Normalize(backend.Priority),
            Labels = backend.Labels is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(backend.Labels, StringComparer.Ordinal),
        };

    public static UpstreamAuthConfig? CloneAuth(UpstreamAuthConfig? auth) =>
        auth is null
            ? null
            : new UpstreamAuthConfig { Type = auth.Type, EnvVar = auth.EnvVar, SecretRef = auth.SecretRef };

    private static ModelBackendConfig FromTopLevel(ModelConfig model) =>
        new()
        {
            Name = DefaultName,
            Url = model.Url.Trim(),
            UpstreamAuth = CloneAuth(model.UpstreamAuth),
            MaxContextLength = model.MaxContextLength,
        };
}
