namespace Autobahn.Persistence.Entities;
/// <summary>One successful read of a paid provider's remaining credit, kept to reconcile against the gateway's charges.</summary>
public sealed class ProviderBalanceReadingEntity
{
    public Guid Id { get; set; }
    public required string Provider { get; set; }
    public DateTimeOffset ReadAt { get; set; }
    public decimal Remaining { get; set; }
    public required string Currency { get; set; }
}
