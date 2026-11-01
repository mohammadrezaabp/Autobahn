using Autobahn.Domain.Validation;

namespace Autobahn.Domain.Models;

public static class ModelConfigValidation
{
    public const int MaxUpstreamModelLength = 256;

    public static bool TryValidate(ModelConfig model, out string? error)
    {
        ArgumentNullException.ThrowIfNull(model);
        error = null;

        // The id is deliberately not checked here: an update body may leave it blank to keep the
        // existing id, and creation paths enforce it themselves.

        if (!ModelTypes.TryNormalize(model.ModelType, out _, out error))
        {
            return false;
        }

        if (!ModelRouteStates.TryNormalize(model.State, out _, out error))
        {
            return false;
        }

        if (!ModelProviders.TryNormalize(model.Provider, out _, out error))
        {
            return false;
        }

        if (!ModelBackends.TryValidateLabels(model.Labels, out error))
        {
            return false;
        }

        // The one-url shape: the top-level fields are the model's only backend.
        if (model.Backends is null)
        {
            return TryValidateUpstream(model.Url, model.UpstreamAuth, model.Id, ModelBackends.DefaultName, out error);
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var backend in model.Backends)
        {
            if (backend is null)
            {
                error = "backends must not contain null.";
                return false;
            }

            var name = backend.Name?.Trim();
            if (!ModelBackends.IsValidName(name))
            {
                error = "a backend's name must be 1 to 63 letters, digits, '.', '_' or '-', starting with a letter or digit.";
                return false;
            }

            if (!names.Add(name!))
            {
                error = $"backend name '{name}' is used more than once.";
                return false;
            }

            if (backend.MaxContextLength < 0)
            {
                error = $"backend '{name}': maxContextLength must not be negative.";
                return false;
            }

            if (backend.UpstreamModel is { Length: > MaxUpstreamModelLength })
            {
                error = $"backend '{name}': upstreamModel must be at most {MaxUpstreamModelLength} characters.";
                return false;
            }

            if (!BackendPriorities.TryNormalize(backend.Priority, out _, out var stateError) ||
                !ModelRouteStates.TryNormalize(backend.State, out _, out stateError) ||
                !ModelBackends.TryValidateLabels(backend.Labels, out stateError) ||
                !TryValidateUpstream(backend.Url, backend.UpstreamAuth, model.Id, name!, out stateError))
            {
                error = $"backend '{name}': {stateError}";
                return false;
            }
        }

        return true;
    }

    /// <summary>Validates one backend on its own, as the backend endpoints write it.</summary>
    public static bool TryValidateBackend(ModelBackendConfig backend, string modelId, out string? error)
    {
        ArgumentNullException.ThrowIfNull(backend);
        return TryValidate(new ModelConfig { Id = modelId, Backends = [backend] }, out error);
    }

    private static bool TryValidateUpstream(
        string? rawUrl,
        UpstreamAuthConfig? upstreamAuth,
        string modelId,
        string backendName,
        out string? error)
    {
        error = null;

        // Callers used to check only that url was non-blank, so "not a url" or "ftp://…" was
        // accepted into the registry and only failed per request in the forwarder (and threw in the
        // health checker's Uri constructor). Reject it at write time instead.
        if (string.IsNullOrWhiteSpace(rawUrl) ||
            !Uri.TryCreate(rawUrl.Trim(), UriKind.Absolute, out var url) ||
            (!string.Equals(url.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(url.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            error = "url must be an absolute http or https URL.";
            return false;
        }

        if (upstreamAuth is null)
        {
            return true;
        }

        if (!string.Equals(upstreamAuth.Type, "bearer", StringComparison.OrdinalIgnoreCase))
        {
            error = "upstreamAuth.type must be 'bearer'.";
            return false;
        }

        var hasEnv = !string.IsNullOrWhiteSpace(upstreamAuth.EnvVar);
        var hasRef = !string.IsNullOrWhiteSpace(upstreamAuth.SecretRef);

        if (hasEnv && hasRef)
        {
            error = "upstreamAuth cannot set both envVar and secretRef.";
            return false;
        }

        if (!hasEnv && !hasRef)
        {
            error = "upstreamAuth requires envVar or secretRef.";
            return false;
        }

        if (hasRef)
        {
            if (!UpstreamSecretRefs.IsValidForBackend(upstreamAuth.SecretRef, modelId, backendName))
            {
                error = ModelBackends.IsDefault(backendName)
                    ? "upstreamAuth.secretRef must be 'file:model:{modelId}' matching this model's id."
                    : "upstreamAuth.secretRef must be 'file:model:{modelId}:backend:{name}' matching this model's id and the backend's name.";
                return false;
            }

            return true;
        }

        return EnvVarNameValidator.TryValidate(upstreamAuth.EnvVar, out _, out error);
    }
}
