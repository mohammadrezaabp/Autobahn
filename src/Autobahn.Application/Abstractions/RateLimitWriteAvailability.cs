using Autobahn.Domain.Configuration;
using Autobahn.Domain.RateLimiting;

namespace Autobahn.Application.Abstractions;
/// <param name = "Writable">Whether a save can be persisted.</param>
/// <param name = "ReasonCode">
/// Null when writable. Otherwise a stable code: <c>store_unavailable</c> — no database is configured,
/// so limits come from appsettings and cannot be changed from the console.
/// </param>
public sealed record RateLimitWriteAvailability(bool Writable, string? ReasonCode)
{
    public const string StoreUnavailable = "store_unavailable";
    public static RateLimitWriteAvailability Available { get; } = new(true, null);
}
