using System.Text.Json.Serialization;

namespace Autobahn.Api.Models;
public sealed class OpenAiModelResponse
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("object")]
    public string Object { get; init; } = "model";

    [JsonPropertyName("created")]
    public long Created { get; init; }

    [JsonPropertyName("owned_by")]
    public string OwnedBy { get; init; } = "llm-gateway";

    [JsonPropertyName("permission")]
    public IReadOnlyList<OpenAiModelPermission> Permission { get; init; } = [];

    [JsonPropertyName("root")]
    public required string Root { get; init; }

    [JsonPropertyName("parent")]
    public string? Parent { get; init; }

    [JsonPropertyName("max_model_len")]
    public int MaxModelLen { get; init; }

    [JsonPropertyName("available")]
    public bool Available { get; init; }
}
