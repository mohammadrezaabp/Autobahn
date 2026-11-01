using System.Text.Json;
using Autobahn.Domain.Models;
using Autobahn.Persistence.Entities;

namespace Autobahn.Persistence.Mapping;

internal static class ModelRouteEntityMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static ModelConfig ToModel(ModelRouteEntity entity) => ModelBackends.Normalize(new ModelConfig
    {
        Id = entity.ModelId,
        Url = entity.Url,
        MaxContextLength = entity.MaxContextLength,
        // Deep-clone the lists so callers cannot mutate tracked entity state.
        Aliases = [.. entity.Aliases],
        Capabilities = [.. entity.Capabilities],
        ModelType = entity.ModelType,
        Provider = entity.Provider,
        State = ModelRouteStates.Normalize(entity.State),
        PublicAccess = entity.PublicAccess,
        UpstreamAuth = string.IsNullOrWhiteSpace(entity.UpstreamAuthJson)
            ? null
            : JsonSerializer.Deserialize<UpstreamAuthConfig>(entity.UpstreamAuthJson, JsonOptions),
        // Null is a row from before the column existed: the model's one backend is then rebuilt
        // from the url, context length and credential above.
        Backends = string.IsNullOrWhiteSpace(entity.BackendsJson)
            ? null
            : JsonSerializer.Deserialize<List<ModelBackendConfig>>(entity.BackendsJson, JsonOptions),
        Labels = string.IsNullOrWhiteSpace(entity.LabelsJson)
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : JsonSerializer.Deserialize<Dictionary<string, string>>(entity.LabelsJson, JsonOptions)
              ?? new Dictionary<string, string>(StringComparer.Ordinal),
    });

    public static ModelRouteEntity ToEntity(ModelConfig source, DateTimeOffset now)
    {
        // The list shape, on a copy: the url, context length and credential columns are then the
        // first backend's, which is what a build without the Backends column reads.
        var model = ModelBackends.Normalize(new ModelConfig
        {
            Id = source.Id,
            Url = source.Url,
            UpstreamAuth = ModelBackends.CloneAuth(source.UpstreamAuth),
            MaxContextLength = source.MaxContextLength,
            Aliases = source.Aliases,
            Capabilities = source.Capabilities,
            ModelType = source.ModelType,
            Provider = source.Provider,
            State = source.State,
            PublicAccess = source.PublicAccess,
            Backends = source.Backends?.Select(ModelBackends.Clone).ToList(),
            Labels = source.Labels,
        });

        return new ModelRouteEntity
        {
            Id = Guid.NewGuid(),
            ModelId = model.Id,
            Url = model.Url,
            MaxContextLength = model.MaxContextLength,
            Aliases = [.. model.Aliases],
            Capabilities = [.. model.Capabilities],
            ModelType = model.ModelType,
            Provider = model.Provider,
            State = ModelRouteStates.Normalize(model.State),
            PublicAccess = model.PublicAccess,
            UpstreamAuthJson = model.UpstreamAuth is null
                ? null
                : JsonSerializer.Serialize(model.UpstreamAuth, JsonOptions),
            BackendsJson = JsonSerializer.Serialize(model.Backends, JsonOptions),
            LabelsJson = model.Labels is { Count: > 0 } ? JsonSerializer.Serialize(model.Labels, JsonOptions) : null,
            UpdatedAt = now,
        };
    }
}
