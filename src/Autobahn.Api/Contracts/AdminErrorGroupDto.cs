using Autobahn.Domain.Models;

namespace Autobahn.Api.Contracts;
/// <summary>One recurring failure, as rendered by a row of the admin Errors tab.</summary>
public sealed class AdminErrorGroupDto
{
    public required string Fingerprint { get; init; }
    public long Count { get; init; }
    public DateTimeOffset FirstSeenUtc { get; init; }
    public DateTimeOffset LastSeenUtc { get; init; }
    public required string Level { get; init; }
    public required string Message { get; init; }
    public string? ExceptionType { get; init; }
    public string? ErrorCode { get; init; }
    public int StatusCode { get; init; }
    public string? ModelId { get; init; }
    public string? EndpointMethod { get; init; }
    public string? EndpointPath { get; init; }
    public string? UpstreamTarget { get; init; }
    public string? Hint { get; init; }
    public string? LastRequestId { get; init; }
    /// <summary>
    /// Which subsystem captured this: <c>proxy</c>, <c>exception</c>, <c>log</c> or <c>modeltest</c>.
    /// </summary>
    /// <remarks>
    /// Taken from the sample rather than aggregated because <c>Source:Category</c> is a fingerprint
    /// component — every occurrence in a group necessarily shares both. Worth surfacing: it is what
    /// tells an operator whether the blank model/status/endpoint columns mean "not captured" or
    /// "this failure never belonged to a request in the first place".
    /// </remarks>
    public string? Source { get; init; }
    /// <summary>The logger category or middleware that reported it.</summary>
    public string? Category { get; init; }
    /// <summary>Stack trace of the most recent occurrence, so the row expands without a second fetch.</summary>
    public string? StackTrace { get; init; }
    public string? UpstreamBodySnippet { get; init; }

    public static AdminErrorGroupDto From(GatewayErrorGroup group) => new()
    {
        Fingerprint = group.Fingerprint,
        Count = group.Count,
        FirstSeenUtc = group.FirstSeen,
        LastSeenUtc = group.LastSeen,
        Level = group.Level,
        Message = group.Message,
        ExceptionType = group.ExceptionType,
        ErrorCode = group.EventCode,
        StatusCode = group.StatusCode,
        ModelId = group.ModelId,
        EndpointMethod = group.Method,
        EndpointPath = group.Path,
        UpstreamTarget = group.UpstreamTarget,
        Hint = group.Hint,
        LastRequestId = group.LastRequestId,
        Source = group.Sample?.Source,
        Category = group.Sample?.Category,
        StackTrace = group.Sample?.StackTrace,
        UpstreamBodySnippet = group.Sample?.UpstreamBodySnippet,
    };
}
