namespace Autobahn.Domain.Models;
public sealed class GatewayBackendHealthEntry
{
    public required string ModelId { get; init; }
    public required string Url { get; init; }
    public bool IsHealthy { get; init; }
    public DateTimeOffset? LastChecked { get; init; }
    public string? Error { get; init; }
}
