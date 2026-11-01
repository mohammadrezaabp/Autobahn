namespace Autobahn.Persistence.Entities;
/// <summary>
/// One day's spend of one key on one model at one paid provider. Written by the usage writer
/// beside the ledger row; read for the monthly caps and the Spend tab.
/// </summary>
public sealed class ProviderSpendDailyEntity
{
    public Guid Id { get; set; }
    public DateOnly UsageDate { get; set; }
    /// <summary><see cref = "Guid.Empty"/> for keyless traffic (never NULL, so the unique index holds).</summary>
    public Guid TenantId { get; set; }
    /// <inheritdoc cref = "TenantId"/>
    public Guid ApiKeyId { get; set; }
    public required string Provider { get; set; }
    public required string ModelId { get; set; }
    public decimal Cost { get; set; }
    public int RequestCount { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
