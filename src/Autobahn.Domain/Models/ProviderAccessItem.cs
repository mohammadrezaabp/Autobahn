namespace Autobahn.Domain.Models;
public sealed class ProviderAccessItem
{
    public required string Provider { get; init; }
    public required string DisplayName { get; init; }
    /// <summary>True for a provider that bills per request; its switches start closed.</summary>
    public bool Paid { get; init; }
    /// <summary>Whether the provider is open at the level asked about: the tenant, or the key.</summary>
    public bool Open { get; init; }
    /// <summary>
    /// Whether the tenant is opened for the provider. A key's switch has no effect, and cannot be
    /// turned on, while this is false.
    /// </summary>
    public bool TenantOpen { get; init; }
    /// <summary>For a tenant: how many of its keys are opened for the provider.</summary>
    public int OpenKeyCount { get; init; }
    /// <summary>
    /// USD the level asked about (the tenant, or the key) may spend at the provider in a calendar
    /// month; null for no cap of its own.
    /// </summary>
    public decimal? MonthlyCapUsd { get; init; }
    /// <summary>What that level has spent at the provider this month (UTC).</summary>
    public decimal SpentThisMonthUsd { get; init; }
    /// <summary>The tenant's cap, which a key's cap cannot exceed; null when the tenant has none.</summary>
    public decimal? TenantMonthlyCapUsd { get; init; }
    public decimal TenantSpentThisMonthUsd { get; init; }
    /// <summary>
    /// False for a provider whose API the gateway does not forward yet (speech). Its switches and
    /// caps are kept, and nothing can be called through them.
    /// </summary>
    public bool Forwardable { get; init; } = true;
}
