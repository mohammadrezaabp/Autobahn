using Autobahn.Domain.RateLimiting;

namespace Autobahn.Domain.Configuration;
/// <summary>CORS section of the config snapshot: the normalized allowed origins (may be empty = deny).</summary>
public sealed record CorsConfigSection
{
    public IReadOnlyList<string> AllowedOrigins { get; init; } = [];
    public static CorsConfigSection Empty { get; } = new();
}
