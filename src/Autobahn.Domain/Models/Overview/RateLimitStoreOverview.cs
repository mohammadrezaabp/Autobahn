namespace Autobahn.Domain.Models.Overview;
/// <param name = "Ratio">
/// The fuller of the two partition tables over the ceiling (floored at one) — the exact expression
/// of the Prometheus alert <c>GatewayRateLimitPartitionsNearCeiling</c>.
/// </param>
public sealed record RateLimitStoreOverview(int RequestPartitions = 0, int StreamPartitions = 0, int MaxPartitions = 0, double Ratio = 0);
