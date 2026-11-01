namespace Autobahn.Domain.Models;
/// <summary>One tenant's standing at a provider: the row of the Access tab.</summary>
public sealed class ProviderTenantAccessRow
{
    public Guid TenantId { get; init; }
    public required string TenantSlug { get; init; }
    public required string TenantName { get; init; }
    public bool Open { get; init; }
    public decimal? MonthlyCapUsd { get; init; }
    public decimal SpentThisMonthUsd { get; init; }
    public int OpenKeyCount { get; init; }
}
