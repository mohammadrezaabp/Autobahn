using System.Text.Json.Serialization;

namespace Autobahn.Api.Models;
public sealed class OpenAiModelPermission
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("object")]
    public string Object { get; init; } = "model_permission";

    [JsonPropertyName("created")]
    public long Created { get; init; }

    [JsonPropertyName("allow_create_engine")]
    public bool AllowCreateEngine { get; init; }

    [JsonPropertyName("allow_sampling")]
    public bool AllowSampling { get; init; } = true;

    [JsonPropertyName("allow_logprobs")]
    public bool AllowLogprobs { get; init; } = true;

    [JsonPropertyName("allow_search_indices")]
    public bool AllowSearchIndices { get; init; }

    [JsonPropertyName("allow_view")]
    public bool AllowView { get; init; } = true;

    [JsonPropertyName("allow_fine_tuning")]
    public bool AllowFineTuning { get; init; }

    [JsonPropertyName("organization")]
    public string Organization { get; init; } = "*";

    [JsonPropertyName("group")]
    public string? Group { get; init; }

    [JsonPropertyName("is_blocking")]
    public bool IsBlocking { get; init; }
}
