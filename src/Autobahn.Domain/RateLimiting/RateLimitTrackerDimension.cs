namespace Autobahn.Domain.RateLimiting;
/// <param name = "Name"><c>tenants</c>, <c>models</c>, <c>apiKeys</c>, <c>tenantModels</c>, <c>violations</c> or <c>limits</c>.</param>
/// <param name = "TrackedKeys">Keys currently held.</param>
/// <param name = "MaxKeys">Ceiling for this dimension.</param>
/// <param name = "AtCapacity">No new key can be added. Not by itself a loss: see <paramref name = "DroppedDecisions"/>.</param>
/// <param name = "DroppedDecisions">
/// Decisions not counted in this dimension because their key was new and the dimension was full.
/// Decisions, not subjects: which subjects were turned away is exactly what is not stored.
/// </param>
/// <param name = "FirstDroppedUtc">When the first one was dropped; null when none has been.</param>
public sealed record RateLimitTrackerDimension(string Name, int TrackedKeys, int MaxKeys, bool AtCapacity, long DroppedDecisions, DateTimeOffset? FirstDroppedUtc);
