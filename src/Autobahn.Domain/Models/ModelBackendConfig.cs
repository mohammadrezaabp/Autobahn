using System.Text.Json.Serialization;

namespace Autobahn.Domain.Models;
/// <summary>
/// One upstream of a model: where it is, how to authenticate to it, how much context it holds and
/// whether it is in service. A model's backends are interchangeable for a request they can hold.
/// </summary>
public sealed class ModelBackendConfig
{
    /// <summary>Unique within the model; <see cref = "ModelBackends.DefaultName"/> for the only backend of an old-shape model.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = ModelBackends.DefaultName;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("upstreamAuth")]
    public UpstreamAuthConfig? UpstreamAuth { get; set; }

    /// <summary>The longest prompt this backend holds, in tokens; 0 means no limit is known.</summary>
    [JsonPropertyName("maxContextLength")]
    public int MaxContextLength { get; set; }

    /// <summary>
    /// <see cref = "ModelRouteStates"/>, for this backend alone. A stopped backend receives no new
    /// requests and is not probed; the ones it is already answering finish.
    /// </summary>
    [JsonPropertyName("state")]
    public string State { get; set; } = ModelRouteStates.Serving;

    [JsonPropertyName("labels")]
    public Dictionary<string, string> Labels { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// The model name sent to this backend, when it is not the model's own id: a self-hosted
    /// <c>Qwen3.8-27B</c> is <c>qwen/qwen3.8-27b</c> at a provider. Null sends the model's id.
    /// </summary>
    [JsonPropertyName("upstreamModel")]
    public string? UpstreamModel { get; set; }

    /// <summary>
    /// <see cref = "BackendPriorities"/>. Primary backends share the model's traffic. A standby takes
    /// a request only when no primary can: all of them unhealthy, stopped, or behind an open circuit.
    /// </summary>
    [JsonPropertyName("priority")]
    public string Priority { get; set; } = BackendPriorities.Primary;

    public bool IsServing() => !ModelRouteStates.IsStopped(State);
    public bool IsStandby() => BackendPriorities.IsStandby(Priority);
}
