namespace Autobahn.Domain.Models.Overview;
/// <summary>Admission decisions in one trailing window.</summary>
/// <param name = "RefusalShare">Refused over decisions in <c>[0, 1]</c>; zero when there were no decisions.</param>
public sealed record RateLimitRefusalWindow(int Minutes = 0, long Decisions = 0, long Admitted = 0, long Refused = 0, long RefusedByRate = 0, long RefusedByStreams = 0, double RefusalShare = 0);
