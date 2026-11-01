using System.Text.Json.Serialization;

namespace Autobahn.Domain.Models;
public sealed class UpstreamAuthConfig
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "bearer";

    [JsonPropertyName("envVar")]
    public string EnvVar { get; set; } = string.Empty;

    [JsonPropertyName("secretRef")]
    public string SecretRef { get; set; } = string.Empty;
}
