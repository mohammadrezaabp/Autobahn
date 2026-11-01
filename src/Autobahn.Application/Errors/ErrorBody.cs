using System.Text.Json.Serialization;

namespace Autobahn.Application.Errors;
public sealed class ErrorBody
{
    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("code")]
    public required string Code { get; init; }

    [JsonPropertyName("param")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Param { get; init; }

    [JsonPropertyName("details")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, object>? Details { get; init; }
}
