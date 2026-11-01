namespace Autobahn.Domain.RateLimiting;
public sealed record AdaptiveModelRow(string ModelId, double Factor, double Saturation, string Reason, DateTimeOffset UpdatedUtc);
