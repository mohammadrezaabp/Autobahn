using Autobahn.Domain.Configuration;
using Autobahn.Domain.RateLimiting;

namespace Autobahn.Api.Contracts;
/// <summary>
/// GET response and PUT request body for <c>/admin/api/rate-limits</c>.
/// </summary>
public sealed class AdminRateLimitsDto
{
    /// <summary>
    /// Global master switch. When false the gateway enforces no request-rate or stream-concurrency
    /// limits. Tier values below are still persisted so the configuration survives a disable/enable cycle.
    /// </summary>
    public bool Enabled { get; set; } = true;
    /// <summary>
    /// Whether load-aware adaptation may hold model limits below their configured rate while a model
    /// is saturated. Never raises a limit; see the adaptive section of the usage report for what it
    /// is currently doing and why.
    /// </summary>
    public bool AdaptiveEnabled { get; set; }
    public RateLimitTierOptions Default { get; set; } = new();
    public Dictionary<string, RateLimitTierOptions> Plans { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// The scoped rules: per-model, per-key, combined, and the global, auth-failure and anonymous
    /// singletons.
    /// </summary>
    /// <remarks>
    /// Null on a PUT means "leave the stored rules alone", which is what a client written against
    /// the older contract sends. An empty array means "there are no rules" and does delete them, so
    /// the two are deliberately different: a client that cannot see rules must not be able to
    /// destroy them by omission.
    /// </remarks>
    public List<AdminRateLimitRuleDto>? Rules { get; set; }
    /// <summary>
    /// The configuration version this body was read at. Response-only; ignored on a PUT.
    /// </summary>
    /// <remarks>
    /// The same value as the <c>ETag</c> on the GET, carried in the body as well so a browser client
    /// can read it without exposing response headers to script. The write precondition is the
    /// <c>If-Match</c> header alone — a version echoed in a request body is not a precondition, it is
    /// just another field the caller controls.
    /// </remarks>
    public long Version { get; set; }
    /// <summary>
    /// Whether a PUT could be persisted right now. Response-only; ignored on a PUT. Null on a
    /// request body, so a client echoing the GET back does not have to strip it.
    /// </summary>
    public bool? Writable { get; set; }
    /// <summary>
    /// Why not, as a stable code, when <see cref = "Writable"/> is false: <c>store_unavailable</c>
    /// (no database is configured; limits come from appsettings). Null otherwise.
    /// </summary>
    public string? ReadOnlyReason { get; set; }
}
