using System.Text.Json.Serialization;

namespace Autobahn.Domain.Configuration;
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";
    /// <summary>
    /// Seed value for the global rate-limiting master switch. Only used to populate a fresh database;
    /// once seeded the live value comes from the config snapshot and is edited via the admin UI.
    /// </summary>
    public bool Enabled { get; set; } = true;
    public RateLimitTierOptions Default { get; set; } = new();
    public Dictionary<string, RateLimitTierOptions> Plans { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, RateLimitTierOptions> Tenants { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// A ceiling on all inference traffic through the gateway, whoever sends it. Left unset (zero
    /// rpm, zero streams) by default: it is a blunt instrument, and a gateway fronting several
    /// backends usually wants per-model limits instead. Set it when the gateway process itself — not
    /// any one upstream — is the resource you are protecting.
    /// </summary>
    public RateLimitTierOptions Global { get; set; } = RateLimitTierOptions.Unset();
    /// <summary>
    /// Per-model limits, keyed by canonical model id (aliases resolve to it before the rule is
    /// looked up). Summed across every caller, so this is the model's own capacity rather than any
    /// one tenant's share of it.
    /// </summary>
    public Dictionary<string, RateLimitTierOptions> Models { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Per-API-key limits, keyed by key id. Bounds one credential inside its tenant's allowance, so
    /// a runaway or compromised key cannot spend the whole tenant budget.
    /// </summary>
    public Dictionary<string, RateLimitTierOptions> ApiKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Combined per-tenant-per-model limits, keyed <c>tenantId|modelId</c>. The narrowest limit an
    /// operator normally needs: "this customer may have 20 rpm of the expensive model, and its usual
    /// allowance of everything else".
    /// </summary>
    public Dictionary<string, RateLimitTierOptions> TenantModels { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Combined per-key-per-model limits, keyed <c>apiKeyId|modelId</c>.</summary>
    public Dictionary<string, RateLimitTierOptions> ApiKeyModels { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// The budget for requests that fail authentication, counted per client address block.
    /// </summary>
    /// <remarks>
    /// Its own tier because the default one is not the right shape for it: a default sized for
    /// legitimate traffic (hundreds of rpm) lets one address make hundreds of credential guesses a
    /// minute, which is not a rate limit on guessing in any useful sense. Left at zero rpm the
    /// gateway falls back to the default tier, preserving the behaviour deployments have today.
    /// </remarks>
    public RateLimitTierOptions AuthFailure { get; set; } = new()
    {
        Rpm = 60,
        Burst = 20,
        MaxConcurrentStreams = 0,
    };
    /// <summary>
    /// Whether credential-guessing protection runs at all. Deliberately separate from
    /// <see cref = "Enabled"/>, and separate from the admin UI.
    /// </summary>
    /// <remarks>
    /// The master switch is a traffic-shaping control: an operator flips it during an incident to
    /// stop refusing legitimate clients. It used to disable this limiter too, so the one action
    /// taken under pressure also removed the only ceiling on credential guessing — a security
    /// control switched off as a side effect of a capacity decision. It lives in appsettings rather
    /// than the database because turning it off is a deployment-level choice (a load test that
    /// sends deliberately bad keys), not something to reach for from a console at three in the
    /// morning.
    /// </remarks>
    public bool AuthFailureProtectionEnabled { get; set; } = true;
    /// <summary>
    /// The budget for the gateway's own request-serving surface — <c>/admin/api/*</c> and
    /// <c>GET /v1/models</c> — counted per caller (tenant, or client address block when
    /// unauthenticated).
    /// </summary>
    /// <remarks>
    /// <para>These are requests the gateway answers itself rather than forwards, and nothing bounded
    /// them: a key could poll an admin endpoint that reads the database as fast as it liked. This is
    /// the ceiling on that.</para>
    ///
    /// <para>Deliberately <em>not</em> the caller's inference tier, and deliberately not editable
    /// from the console. A tenant tier is an inference-economics decision — what a customer's plan
    /// buys — and applying it here means an operator who sets a tight default tier can no longer
    /// administer the gateway: the console call that would raise it again is refused by the tier it
    /// is trying to change. This is a safety net against unmetered polling, sized so that nothing a
    /// console does can reach it, and it lives in appsettings alongside the other guard rails. Zero
    /// rpm and zero burst switches it off.</para>
    /// </remarks>
    public RateLimitTierOptions ControlPlane { get; set; } = new()
    {
        Rpm = 600,
        Burst = 120,
        MaxConcurrentStreams = 0,
    };
    /// <summary>
    /// How much more traffic an address that has spent its auth-failure budget may still have
    /// <em>validated</em>, as a multiple of <see cref = "AuthFailure"/>.
    /// </summary>
    /// <remarks>
    /// <para>Past its budget an address is refused unless it can prove a credential, so that a
    /// shared address is not a lockout for the key-holders behind it. Proving one costs a lookup,
    /// and for an attacker rotating keys it costs a database read every time — which would make the
    /// limiter shorten the response without shedding the work it exists to shed. This bounds that:
    /// beyond <c>AuthFailure × this</c> requests a minute from one address, nothing is validated and
    /// everything is refused outright.</para>
    ///
    /// <para>Ten is wide enough that a real client behind a busy NAT is never the one to hit it, and
    /// narrow enough that the validation an attacker can force stays a small multiple of the
    /// guessing rate the operator already chose. One disables the allowance entirely, restoring the
    /// hard lockout the multiplier exists to avoid.</para>
    /// </remarks>
    public int AuthFailureProbeMultiplier { get; set; } = 10;
    /// <summary>
    /// The tier for callers with no credential on a gateway that requires one, counted per client
    /// address block. Only reachable while some model is <c>publicAccess</c>; with authentication
    /// off every caller keeps the default tier.
    /// </summary>
    /// <remarks>
    /// Its own tier because the default one is sized for a paying tenant, and an anonymous address
    /// used to receive exactly that: thousands of requests a minute per address, with no way to
    /// configure anything smaller without also tightening every authenticated tenant on the default
    /// tier. Zero rpm and zero streams means "use the default tier", which is what deployments that
    /// predate the setting get. A zero rpm with a stream cap keeps the default rate and applies
    /// only the cap, the same composition a <c>tenant</c> rule with rpm 0 gets.
    /// </remarks>
    public RateLimitTierOptions Anonymous { get; set; } = new()
    {
        Rpm = 60,
        Burst = 20,
        MaxConcurrentStreams = 2,
    };
    public AdaptiveRateLimitOptions Adaptive { get; set; } = new();
    public int InMemoryPartitionRetentionSeconds { get; set; } = 3600;
    /// <summary>
    /// How often the maintenance service sweeps stale partitions.
    /// </summary>
    /// <remarks>
    /// Sweeping used to happen inline, on whichever request happened to trip an operation counter:
    /// that request paid an O(live partitions) scan, and past the partition ceiling it also paid a
    /// full copy-and-sort of the table. At the default ceiling that is a 50,000-element allocation
    /// and sort on a request thread — invisible in an average, and a very visible p99. A timer costs
    /// the same work at a rate set by wall-clock instead of by traffic, and no request waits for it.
    /// </remarks>
    public int MaintenanceIntervalSeconds { get; set; } = 10;
    /// <summary>
    /// Hard ceiling on live partitions per dimension (request buckets, stream-slot states). Anonymous
    /// traffic partitions by client address block, so without a ceiling a caller spread across an
    /// address range mints one entry per block and holds it for the whole retention window. When the
    /// ceiling is passed, the least-recently-seen partitions are evicted down to it. Evicting a
    /// partition resets its bucket, so keep this comfortably above the number of clients you expect.
    /// </summary>
    public int InMemoryMaxPartitions { get; set; } = 50_000;
    /// <summary>
    /// How many distinct keys the usage report tracks per section. Past it, new keys are ignored
    /// rather than evicting existing ones, so a flood of one-off callers cannot push the tenants an
    /// operator is watching out of the report.
    /// </summary>
    public int UsageReportMaxKeys { get; set; } = 500;
    /// <summary>
    /// How often the spent token buckets are written to the database, so a restart carries on from
    /// the fill the last process left. 0 turns it off, and every bucket then starts full after a
    /// restart. Needs a database; without one nothing is written.
    /// </summary>
    /// <remarks>
    /// A bucket restored from a snapshot refills for the time that passed since it was written, so
    /// the interval bounds what a restart forgives: at most this many seconds of spending.
    /// </remarks>
    public int BucketSnapshotIntervalSeconds { get; set; } = 10;
    /// <summary>Most buckets written per snapshot. Full buckets are never written.</summary>
    public int BucketSnapshotMaxBuckets { get; set; } = 5000;
    /// <summary>
    /// Completion tokens a token limit reserves for a request that sets no <c>max_tokens</c>. The
    /// difference from what the response actually used is handed back when it ends.
    /// </summary>
    /// <remarks>
    /// The same default the budget reservation uses, so one request is estimated the same way by
    /// both. Too low and concurrent long answers overshoot a token limit before any of them settles;
    /// too high and a caller that leaves <c>max_tokens</c> out is refused with budget to spare.
    /// </remarks>
    public int TokenReservationDefaultMaxTokens { get; set; } = 4096;
}
