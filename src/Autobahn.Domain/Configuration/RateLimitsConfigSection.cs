using Autobahn.Domain.RateLimiting;

namespace Autobahn.Domain.Configuration;
/// <summary>
/// Rate-limit section of the config snapshot.
/// </summary>
/// <remarks>
/// <para>The scopes here compose rather than override one another: a request is admitted only when
/// every scope that applies to it admits it. Adding a narrower rule can therefore only tighten what
/// a caller may do, which is what makes the outcome independent of the order rules were configured
/// in.</para>
///
/// <para>Precedence applies <em>within</em> the tenant scope only, where three sources can each name
/// a tier for the same caller: a per-tenant override wins, then the tenant's plan, then the default.
/// Every other scope has exactly one source, so there is nothing to resolve. All keys are compared
/// OrdinalIgnoreCase.</para>
///
/// <para><see cref = "TenantOverrides"/>, <see cref = "Models"/> and the combined maps are populated
/// from the database on a database-backed deployment and from the <c>RateLimiting</c> section of
/// appsettings on a database-less one; both sources produce the same shape, and the database wins
/// where a deployment has one.</para>
/// </remarks>
public sealed record RateLimitsConfigSection
{
    /// <summary>
    /// Declared before <see cref = "Defaults"/> on purpose. Static field initialisers run in
    /// declaration order, so a <c>Defaults</c> built above this line captures a null for every map
    /// that defaults to it — and the symptom is a NullReferenceException from whichever consumer
    /// first enumerates one, far from the cause.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, RateLimitPolicy> EmptyMap = new Dictionary<string, RateLimitPolicy>(StringComparer.OrdinalIgnoreCase);
    /// <summary>Declared before <see cref = "Defaults"/> for the same reason as <see cref = "EmptyMap"/>.</summary>
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<RateLimitWindowDefinition>> EmptySchedules = new Dictionary<string, IReadOnlyList<RateLimitWindowDefinition>>(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Global master switch. When false, neither request-rate limits nor stream-concurrency limits are
    /// enforced for any tier. Lives on the section rather than on <see cref = "RateLimitPolicy"/> because
    /// it is gateway-wide; the per-tier numbers stay meaningful and are restored when re-enabled.
    /// Defaults to true so a database-less deployment enforces limits exactly as before.
    /// </summary>
    public bool Enabled { get; init; } = true;
    /// <summary>
    /// Whether load-aware adaptation may reduce the configured tiers. Separate from
    /// <see cref = "Enabled"/> so an operator can switch off the clever half without switching off
    /// enforcement — the order you want in an incident.
    /// </summary>
    public bool AdaptiveEnabled { get; init; }
    public RateLimitPolicy Default { get; init; } = RateLimitPolicy.Default;

    /// <summary>
    /// Whether any tier in this section limits tokens per minute. Walks every map, so a caller on
    /// the request path keeps the answer for as long as it holds the same section.
    /// </summary>
    public bool HasTokenLimits() => Default.EnforcesTokens || Global.EnforcesTokens || Anonymous.EnforcesTokens || AnyTokens(Plans) || AnyTokens(TenantOverrides) || AnyTokens(Models) || AnyTokens(ApiKeys) || AnyTokens(TenantModels) || AnyTokens(ApiKeyModels);
    private static bool AnyTokens(IReadOnlyDictionary<string, RateLimitPolicy> tiers)
    {
        foreach (var tier in tiers.Values)
        {
            if (tier.EnforcesTokens)
            {
                return true;
            }
        }

        return false;
    }

    public IReadOnlyDictionary<string, RateLimitPolicy> Plans { get; init; } = EmptyMap;
    public IReadOnlyDictionary<string, RateLimitPolicy> TenantOverrides { get; init; } = EmptyMap;
    /// <summary>A ceiling on all inference traffic; <see cref = "RateLimitPolicy.Unlimited"/> when unset.</summary>
    public RateLimitPolicy Global { get; init; } = RateLimitPolicy.Unlimited;
    /// <summary>Per-model limits, keyed by canonical model id.</summary>
    public IReadOnlyDictionary<string, RateLimitPolicy> Models { get; init; } = EmptyMap;
    /// <summary>Per-API-key limits, keyed by key id.</summary>
    public IReadOnlyDictionary<string, RateLimitPolicy> ApiKeys { get; init; } = EmptyMap;
    /// <summary>Combined limits keyed <c>tenantId|modelId</c>.</summary>
    public IReadOnlyDictionary<string, RateLimitPolicy> TenantModels { get; init; } = EmptyMap;
    /// <summary>Combined limits keyed <c>apiKeyId|modelId</c>.</summary>
    public IReadOnlyDictionary<string, RateLimitPolicy> ApiKeyModels { get; init; } = EmptyMap;
    /// <summary>
    /// The budget for requests authentication refuses, per client address block.
    /// <see cref = "RateLimitPolicy.Unlimited"/> means "use the default tier", which is what
    /// deployments that never configured one get.
    /// </summary>
    public RateLimitPolicy AuthFailure { get; init; } = RateLimitPolicy.Unlimited;
    /// <summary>
    /// The tier for callers with no credential, per client address block.
    /// <see cref = "RateLimitPolicy.Unlimited"/> means "use the default tier", which is what
    /// deployments that never configured one get.
    /// </summary>
    public RateLimitPolicy Anonymous { get; init; } = RateLimitPolicy.Unlimited;
    /// <summary>
    /// Schedule windows per scoped rule, keyed by the rule's <c>scope:target</c> identity (see
    /// <see cref = "RateLimitScheduleProjection.Identity"/>). Part of the <em>stored</em> shape: the
    /// maps above hold base tiers here, and effective tiers on the projected copy the request path
    /// reads (see <see cref = "Stored"/>).
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<RateLimitWindowDefinition>> Schedules { get; init; } = EmptySchedules;
    /// <summary>
    /// Rules an operator has switched off, keyed by the same <c>scope:target</c> identity as
    /// <see cref = "Schedules"/>, holding the tier each would enforce if switched back on.
    /// </summary>
    /// <remarks>
    /// A side-car rather than a flag inside the maps above, because those maps <em>are</em> what the
    /// request path reads: anything present in them is enforced, and a rule that must enforce nothing
    /// therefore has to be absent from them. It cannot simply be dropped either — the admin surface
    /// rebuilds its rule list from these maps, so a rule that existed nowhere could not be listed,
    /// edited or switched back on. Keeping the tier here is what lets a disabled rule come back
    /// exactly as it was. Its windows stay in <see cref = "Schedules"/> under the same key.
    /// </remarks>
    public IReadOnlyDictionary<string, RateLimitPolicy> DisabledRules { get; init; } = EmptyMap;
    /// <summary>
    /// On a projected section, the stored section it was projected from; null on a stored section.
    /// The admin surface reads base tiers from here so an active window never shows up as the
    /// configured number.
    /// </summary>
    public RateLimitsConfigSection? Stored { get; init; }
    /// <summary>
    /// Bumped by every projection, so a cache keyed on configuration misses when a window begins or
    /// ends exactly as it misses on an admin write. Zero on a section with no schedules.
    /// </summary>
    public long EffectiveVersion { get; init; }
    /// <summary>
    /// The version of the rate-limit configuration alone, which is what its ETag names. Separate from
    /// <see cref = "GatewayConfigSnapshot.Version"/>, which every configuration write moves: a CORS
    /// save is not a reason to refuse a rate-limit write as stale.
    /// </summary>
    public long Version { get; init; }
    /// <summary>The stored section: this one, or the one this projection came from.</summary>
    public RateLimitsConfigSection StoredOrSelf => Stored ?? this;
    public bool HasSchedules => Schedules.Count > 0;
    public static RateLimitsConfigSection Defaults { get; } = new();
}
