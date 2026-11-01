using System.Runtime.CompilerServices;

namespace Autobahn.Domain.RateLimiting;
/// <summary>Which control a decision came from.</summary>
public enum RateLimitControl
{
    /// <summary>The request bucket: how many requests per minute a partition may start.</summary>
    Rate = 0,
    /// <summary>The slot count: how many streaming responses a partition may hold open at once.</summary>
    Concurrency = 1,
    /// <summary>The token bucket: how many prompt and completion tokens per minute a partition may spend.</summary>
    Tokens = 2,
}
