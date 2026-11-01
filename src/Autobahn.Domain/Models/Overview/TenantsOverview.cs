namespace Autobahn.Domain.Models.Overview;
/// <summary>Who is using the gateway: top consumers and keys that need attention.</summary>
public sealed record TenantsOverview
{
    public DateTimeOffset BuiltAtUtc { get; init; }
    public string Currency { get; init; } = "USD";
    public int TenantCount { get; init; }
    public int KeyCount { get; init; }
    public int RevokedKeyCount { get; init; }
    /// <summary>
    /// Archived keys, reported apart from <see cref = "KeyCount"/> so filing keys away shrinks the
    /// headline count instead of inflating it.
    /// </summary>
    public int ArchivedKeyCount { get; init; }
    public IReadOnlyList<TenantConsumer> TopConsumersMonthToDate { get; init; } = [];
    public IReadOnlyList<KeySummary> ExpiringKeys { get; init; } = [];
    public IReadOnlyList<KeySummary> IdleKeys { get; init; } = [];
    /// <summary>Share of month-to-date requests made without an API key (public-access models).</summary>
    public double AnonymousRequestShare { get; init; }
}
