using System.Text.Json.Serialization;

namespace Autobahn.Api.Models;
public sealed class OpenAiModelListResponse
{
    [JsonPropertyName("object")]
    public string Object { get; init; } = "list";

    [JsonPropertyName("data")]
    public IReadOnlyList<OpenAiModelResponse> Data { get; init; } = [];

    /// <summary>
    /// Only present on anonymous listings when the gateway requires an API key: a minimal
    /// inventory of every healthy model and whether a key is needed to use it, so a caller
    /// with an empty <c>data</c> can see what the gateway offers and that they must get a key.
    /// Omitted on authenticated responses.
    /// </summary>
    [JsonPropertyName("models")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ModelAvailabilityHint>? Models { get; init; }
}
