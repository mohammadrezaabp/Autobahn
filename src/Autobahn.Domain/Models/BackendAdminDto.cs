namespace Autobahn.Domain.Models;

public sealed class BackendAdminDto
{
    public required string ModelId { get; init; }

    /// <summary>Which of the model's backends this row is; <c>default</c> for a model with one url.</summary>
    public string Backend { get; init; } = ModelBackends.DefaultName;

    public required string Url { get; init; }

    /// <summary>The longest prompt this backend holds, in tokens; 0 when unknown.</summary>
    public int MaxContextLength { get; init; }

    /// <summary>
    /// Requests this gateway process is forwarding to the backend right now. A stopped backend is
    /// safe to take down once this reaches 0.
    /// </summary>
    public int InFlight { get; init; }

    /// <summary><see cref="BackendPriorities"/>: primary, or standby (used only when no primary can serve).</summary>
    public string Priority { get; init; } = BackendPriorities.Primary;

    /// <summary>The model name sent to this backend when it differs from the model's id.</summary>
    public string? UpstreamModel { get; init; }

    /// <summary>Who this backend's requests are billed by (<see cref="ModelProviders"/>).</summary>
    public string Provider { get; init; } = ModelProviders.SelfHosted;

    public IReadOnlyDictionary<string, string> Labels { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public bool IsHealthy { get; init; }

    /// <summary>
    /// Whether an operator has this backend in service (<see cref="ModelRouteStates"/>): stopped
    /// when either the backend or its model is. A stopped backend is not probed, so this is what
    /// tells an operator why its health looks frozen.
    /// </summary>
    public string State { get; init; } = ModelRouteStates.Serving;

    public string? Alias { get; init; }

    /// <summary>HTTP status of the last probe, when it got a response.</summary>
    public int? StatusCode { get; init; }

    /// <summary>Why the last probe failed (or a note such as "auth required"); null when clean.</summary>
    public string? Error { get; init; }

    public DateTimeOffset? LastCheckedUtc { get; init; }

    /// <summary>When the healthy/unhealthy state last flipped.</summary>
    public DateTimeOffset? LastTransitionUtc { get; init; }
}
