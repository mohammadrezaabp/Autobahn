using System.Text.Json.Serialization;

namespace Autobahn.Api.Models;
public sealed class ModelAvailabilityHint
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("api_key_required")]
    public required bool ApiKeyRequired { get; init; }
}
