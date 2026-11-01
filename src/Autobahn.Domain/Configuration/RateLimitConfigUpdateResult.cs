namespace Autobahn.Domain.Configuration;
public sealed class RateLimitConfigUpdateResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public int StatusCode { get; init; } = 200;
    /// <summary>
    /// The rate-limit configuration version the write produced, from the repository rather than from
    /// the refreshed snapshot: the write is committed even when the refresh that follows it is not,
    /// and the caller's next write has to be based on this number either way.
    /// </summary>
    public long? Version { get; init; }

    public static RateLimitConfigUpdateResult Ok(string message, long? version = null) => new()
    {
        Success = true,
        Message = message,
        StatusCode = 200,
        Version = version
    };
    public static RateLimitConfigUpdateResult Fail(string message, int statusCode) => new()
    {
        Success = false,
        Message = message,
        StatusCode = statusCode
    };
}
