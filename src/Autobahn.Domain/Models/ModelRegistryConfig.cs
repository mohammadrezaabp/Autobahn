using System.Text.Json.Serialization;

namespace Autobahn.Domain.Models;

public sealed class ModelRegistryConfig
{
    [JsonPropertyName("models")]
    public List<ModelConfig>? Models { get; set; }
}
