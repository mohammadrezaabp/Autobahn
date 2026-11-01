namespace Autobahn.Domain.Models;
public sealed class GatewayHealthResponse
{
    public required string Status { get; init; }
    public required DateTimeOffset Uptime { get; init; }
    public int TotalBackends { get; init; }
    public int HealthyBackends { get; init; }
    public int UnhealthyBackends { get; init; }
    public IReadOnlyList<GatewayBackendHealthEntry> Backends { get; init; } = [];
}
