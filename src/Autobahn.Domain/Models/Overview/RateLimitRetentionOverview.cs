namespace Autobahn.Domain.Models.Overview;
/// <summary>How much history the counters hold and since when. Process-local: a restart starts over.</summary>
/// <param name = "HistoryMinutes">Per-minute counters kept, oldest dropped first (about three hours).</param>
/// <param name = "TrackingSinceUtc">Counting began here: process start or the last reset.</param>
/// <param name = "ProcessLocal">Always true: the counters are in memory and are neither persisted nor shared between replicas.</param>
public sealed record RateLimitRetentionOverview(int HistoryMinutes = 0, DateTimeOffset? TrackingSinceUtc = null, bool ProcessLocal = true);
