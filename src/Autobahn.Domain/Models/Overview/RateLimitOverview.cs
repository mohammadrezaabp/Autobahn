namespace Autobahn.Domain.Models.Overview;
/// <summary>
/// Rate limiting at a glance: whether it is enforced, what it refused in the last hour and the last
/// five minutes, which limits did the refusing, and how far to trust the counters behind it.
/// </summary>
/// <remarks>
/// Built from the in-memory usage tracker, which is process-local and keeps about three hours of
/// per-minute counters (<see cref = "Retention"/>): nothing here is durable history, and every figure
/// restarts with the gateway. A gateway without the tracker has no such section at all (204); one
/// with the tracker and no traffic gets this record with zeros, which are real zeros.
/// </remarks>
public sealed record RateLimitOverview
{
    public DateTimeOffset BuiltAtUtc { get; init; }
    /// <summary>The master switch as the request path reads it. False means nothing is rate limited.</summary>
    public bool Enforced { get; init; }
    public bool AdaptiveEnabled { get; init; }
    /// <summary>Ordinary scoped rules (global, tenant, key, model and the two pairs); the protective budgets are not counted.</summary>
    public int RuleCount { get; init; }
    /// <summary>Rules an operator switched off; included in <see cref = "RuleCount"/>.</summary>
    public int DisabledRuleCount { get; init; }
    /// <summary>True while a configuration reload is being applied, so a momentary state is not judged as settled.</summary>
    public bool ConfigReloadInProgress { get; init; }
    public RateLimitScheduleOverview Schedule { get; init; } = RateLimitScheduleOverview.Unavailable;
    public RateLimitRetentionOverview Retention { get; init; } = new();
    public RateLimitRefusalWindow LastHour { get; init; } = new();
    public RateLimitRefusalWindow LastFiveMinutes { get; init; } = new();
    /// <summary>Tenants with at least one refusal in the last hour.</summary>
    public int RefusedTenantCount { get; init; }
    /// <summary>API keys with at least one refusal in the last hour.</summary>
    public int RefusedKeyCount { get; init; }
    /// <summary>The subject lists hit the report's row cap, so the counts are lower bounds.</summary>
    public bool RefusedSubjectsTruncated { get; init; }
    public IReadOnlyList<RateLimitRefusedSubject> TopRefusedTenants { get; init; } = [];
    public IReadOnlyList<RateLimitRefusedSubject> TopRefusedKeys { get; init; } = [];
    /// <summary>Configured limits that refused at least once in the last hour.</summary>
    public int RefusingLimitCount { get; init; }
    /// <summary>
    /// Limits that refused in the last hour, most refused first, then limits that refused nothing but
    /// whose busiest minute reached <see cref = "RateLimitOverviewLimits.NearRatio"/> of their rate
    /// (<see cref = "RateLimitLimitOverview.NearLimit"/>); at most six. Near-limit rows are not refusals
    /// and are not counted in <see cref = "RefusingLimitCount"/>.
    /// </summary>
    public IReadOnlyList<RateLimitLimitOverview> Limits { get; init; } = [];
    /// <summary>The two protective budgets, <c>auth_failure</c> then <c>anonymous</c>; always both.</summary>
    public IReadOnlyList<RateLimitProtectiveOverview> Protective { get; init; } = [];
    public RateLimitAdaptiveOverview Adaptive { get; init; } = new();
    public RateLimitTrackerOverview Tracker { get; init; } = new();
    public RateLimitStoreOverview Store { get; init; } = new();
}
