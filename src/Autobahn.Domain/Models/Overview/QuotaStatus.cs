namespace Autobahn.Domain.Models.Overview;
public sealed record QuotaStatus
{
    public required string PartitionKey { get; init; }
    public string? TenantSlug { get; init; }
    public string? PlanSlug { get; init; }
    public string Period { get; init; } = string.Empty;
    public long Used { get; init; }
    public long Limit { get; init; }
    public double Ratio { get; init; }
    public bool NearLimit { get; init; }
    public bool Exceeded { get; init; }
}
