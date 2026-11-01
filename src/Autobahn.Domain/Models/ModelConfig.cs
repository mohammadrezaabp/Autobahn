using System.Text.Json.Serialization;

namespace Autobahn.Domain.Models;
public sealed class ModelConfig
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("upstreamAuth")]
    public UpstreamAuthConfig? UpstreamAuth { get; set; }

    [JsonPropertyName("maxContextLength")]
    public int MaxContextLength { get; set; }

    [JsonPropertyName("aliases")]
    public List<string> Aliases { get; set; } = [];

    /// <summary>
    /// When true, inference may proceed without a valid Autobahn API key (rate limits still apply).
    /// </summary>
    [JsonPropertyName("publicAccess")]
    public bool PublicAccess { get; set; }

    /// <summary>
    /// Supported inference capabilities (e.g. chat, completions, embeddings, rerank).
    /// Empty list means all routes are supported (backward-compatible default).
    /// </summary>
    [JsonPropertyName("capabilities")]
    public List<string> Capabilities { get; set; } = [];

    /// <summary>
    /// What the upstream model does (see <see cref = "ModelTypes"/>): text-generation, embedding,
    /// rerank, ocr, image-generation, video-generation, audio-transcription. Drives the shape of the
    /// admin health check. Null means unset — <see cref = "ModelTypes.Resolve"/> then infers from
    /// <see cref = "Capabilities"/>, so models registered before this field existed still classify.
    /// </summary>
    [JsonPropertyName("modelType")]
    public string? ModelType { get; set; }

    /// <summary>
    /// Who serves the model (see <see cref = "ModelProviders"/>). Null means unset —
    /// <see cref = "ModelProviders.Resolve"/> then reads it from the backend addresses, so a route
    /// written before this field existed is still told apart. Set only to correct that reading.
    /// </summary>
    [JsonPropertyName("provider")]
    public string? Provider { get; set; }

    /// <summary>
    /// Whether the route is in service (see <see cref = "ModelRouteStates"/>). A stopped route is
    /// still registered but is hidden from <c>GET /v1/models</c> and refused at admission, so
    /// stopping a model is reversible where deleting it is not. Routes written before this field
    /// existed deserialize to <see cref = "ModelRouteStates.Serving"/>, which is what they were.
    /// </summary>
    [JsonPropertyName("state")]
    public string State { get; set; } = ModelRouteStates.Serving;

    /// <summary>
    /// The upstreams that serve this model (see <see cref = "ModelBackends"/>). Null is the shape
    /// from before a model could have more than one: <see cref = "Url"/>, <see cref = "UpstreamAuth"/>
    /// and <see cref = "MaxContextLength"/> then describe its only backend. Once the list is set those
    /// three mirror it — the first backend's address and credential, the largest context among the
    /// backends in service — so a reader that knows only the old shape still sees a usable model.
    /// An empty list is a model that is registered and has nothing to forward to.
    /// </summary>
    [JsonPropertyName("backends")]
    public List<ModelBackendConfig>? Backends { get; set; }

    /// <summary>
    /// Free-form marks for whoever registered the route (an orchestrator recognising the routes it
    /// made). The gateway stores and returns them and reads nothing into them.
    /// </summary>
    [JsonPropertyName("labels")]
    public Dictionary<string, string> Labels { get; set; } = new(StringComparer.Ordinal);

    public bool HasCapability(string capability) => Capabilities.Count == 0 || Capabilities.Contains(capability, StringComparer.OrdinalIgnoreCase);
}
