namespace Autobahn.Api.Contracts;
public sealed class GatewayBackendHealthSummaryEntry
{
    public required string ModelId { get; init; }
    public bool IsHealthy { get; init; }
    public DateTimeOffset? LastChecked { get; init; }
}
