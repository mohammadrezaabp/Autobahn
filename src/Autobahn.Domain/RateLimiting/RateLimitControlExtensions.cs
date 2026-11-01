using System.Runtime.CompilerServices;

namespace Autobahn.Domain.RateLimiting;
public static class RateLimitControlExtensions
{
    /// <summary>The control as metrics and reports label it.</summary>
    public static string ToLabel(this RateLimitControl control) => control switch
    {
        RateLimitControl.Concurrency => "concurrency",
        RateLimitControl.Tokens => "tokens",
        _ => "rate",
    };
}
