using System.Text.Json;
using Autobahn.Domain.Models;

namespace Autobahn.Registry.Services;

internal static class ModelRegistryPersistence
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
    };

    internal static ModelRegistryConfig Deserialize(string json) =>
        JsonSerializer.Deserialize<ModelRegistryConfig>(json, JsonOptions)
        ?? throw new JsonException("Model registry configuration deserialized to null.");

    /// <summary>
    /// Validates a candidate model set and builds the id+alias lookup for it. Callers must run this
    /// <em>before</em> persisting: a set that cannot build a lookup is a set the gateway cannot load,
    /// and persisting one leaves a database that poisons every subsequent startup.
    /// </summary>
    /// <remarks>An empty set is valid — a deployment with no routes configured is a legal state.</remarks>
    internal static (Dictionary<string, ModelConfig> Lookup, List<ModelConfig> Models) BuildLookup(
        IReadOnlyList<ModelConfig> source)
    {
        var lookup = new Dictionary<string, ModelConfig>(StringComparer.OrdinalIgnoreCase);
        var models = new List<ModelConfig>(source.Count);

        foreach (var model in source)
        {
            if (string.IsNullOrWhiteSpace(model.Id))
            {
                throw new JsonException("Model entry is missing required 'id'.");
            }

            // A model written with a list may have none (it is registered and forwards nothing);
            // one written in the one-url shape must have its url.
            if (model.Backends is null && string.IsNullOrWhiteSpace(model.Url))
            {
                throw new JsonException($"Model '{model.Id}' is missing required 'url'.");
            }

            ThrowIfBackendsUnloadable(model);

            if (models.Any(m => string.Equals(m.Id, model.Id, StringComparison.OrdinalIgnoreCase)))
            {
                throw new JsonException($"Duplicate model id '{model.Id}'.");
            }

            models.Add(CloneModel(model));
            var canonical = models[^1];
            lookup[canonical.Id] = canonical;

            foreach (var alias in model.Aliases)
            {
                if (string.IsNullOrWhiteSpace(alias))
                {
                    continue;
                }

                if (lookup.TryGetValue(alias, out var existingAlias) && !ReferenceEquals(existingAlias, canonical))
                {
                    throw new JsonException($"Duplicate alias '{alias}' in registry.");
                }

                lookup[alias] = canonical;
            }
        }

        return (lookup, models);
    }

    /// <summary>
    /// Drops entries that would make a persisted set unloadable (blank id/url, duplicate id,
    /// conflicting alias) and reports what was dropped. Used only on the load path, so a database
    /// poisoned by an older build degrades to "serve what is valid, shout about the rest" instead of
    /// leaving the gateway with no routes at all. Writes validate strictly via <see cref="BuildLookup"/>.
    /// </summary>
    internal static (List<ModelConfig> Models, List<string> Problems) Sanitize(IReadOnlyList<ModelConfig> source)
    {
        var models = new List<ModelConfig>(source.Count);
        var problems = new List<string>();
        var claimed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var sourceModel in source)
        {
            var model = sourceModel;
            if (string.IsNullOrWhiteSpace(model.Id))
            {
                problems.Add("Dropped a route with no id.");
                continue;
            }

            if (model.Backends is null && string.IsNullOrWhiteSpace(model.Url))
            {
                problems.Add($"Dropped route '{model.Id}': no url.");
                continue;
            }

            if (model.Backends is not null)
            {
                // Same degrade-and-report rule as for routes: keep the backends that can be
                // loaded, name the ones that cannot.
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var kept = new List<ModelBackendConfig>(model.Backends.Count);
                foreach (var backend in model.Backends)
                {
                    var name = string.IsNullOrWhiteSpace(backend?.Name) ? ModelBackends.DefaultName : backend!.Name.Trim();
                    if (backend is null || string.IsNullOrWhiteSpace(backend.Url))
                    {
                        problems.Add($"Dropped backend '{name}' of route '{model.Id}': no url.");
                        continue;
                    }

                    if (!seen.Add(name))
                    {
                        problems.Add($"Dropped backend '{name}' of route '{model.Id}': that name is used twice.");
                        continue;
                    }

                    kept.Add(backend);
                }

                if (kept.Count != model.Backends.Count)
                {
                    model = CloneModel(model);
                    model.Backends = kept.Select(ModelBackends.Clone).ToList();
                }
            }

            if (claimed.TryGetValue(model.Id, out var idOwner))
            {
                problems.Add($"Dropped route '{model.Id}': that name is already taken by '{idOwner}'.");
                continue;
            }

            var clone = CloneModel(model);
            claimed[clone.Id] = clone.Id;

            var aliases = new List<string>(clone.Aliases.Count);
            foreach (var alias in clone.Aliases)
            {
                if (string.IsNullOrWhiteSpace(alias))
                {
                    continue;
                }

                if (claimed.TryGetValue(alias, out var aliasOwner))
                {
                    problems.Add($"Dropped alias '{alias}' of route '{clone.Id}': already taken by '{aliasOwner}'.");
                    continue;
                }

                claimed[alias] = clone.Id;
                aliases.Add(alias);
            }

            clone.Aliases = aliases;
            models.Add(clone);
        }

        return (models, problems);
    }

    private static void ThrowIfBackendsUnloadable(ModelConfig model)
    {
        if (model.Backends is null)
        {
            return;
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var backend in model.Backends)
        {
            var name = string.IsNullOrWhiteSpace(backend?.Name) ? ModelBackends.DefaultName : backend!.Name.Trim();
            if (backend is null || string.IsNullOrWhiteSpace(backend.Url))
            {
                throw new JsonException($"Backend '{name}' of model '{model.Id}' is missing required 'url'.");
            }

            if (!names.Add(name))
            {
                throw new JsonException($"Duplicate backend name '{name}' in model '{model.Id}'.");
            }
        }
    }

    /// <summary>
    /// A deep copy in the list shape: a model written with one url comes out with its one backend,
    /// and the top-level address, credential and context length restated from the list
    /// (<see cref="ModelBackends.Normalize"/>). Everything the registry holds or hands out has been
    /// through here, so no reader has to ask which shape it was given.
    /// </summary>
    internal static ModelConfig CloneModel(ModelConfig model) =>
        ModelBackends.Normalize(new ModelConfig
        {
            Id = model.Id,
            Url = model.Url,
            UpstreamAuth = ModelBackends.CloneAuth(model.UpstreamAuth),
            MaxContextLength = model.MaxContextLength,
            Aliases = [.. model.Aliases],
            PublicAccess = model.PublicAccess,
            Capabilities = [.. model.Capabilities],
            ModelType = model.ModelType,
            Provider = model.Provider,
            // Normalized on every clone, so a route persisted before this field existed (or by a
            // build that wrote something this one does not recognise) loads as serving rather than
            // as a route with an unresolvable state.
            State = ModelRouteStates.Normalize(model.State),
            Backends = model.Backends?.Where(b => b is not null).Select(ModelBackends.Clone).ToList(),
            Labels = model.Labels is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(model.Labels, StringComparer.Ordinal),
        });
}
