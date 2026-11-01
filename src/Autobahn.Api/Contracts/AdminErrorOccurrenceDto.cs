using Autobahn.Domain.Models;

namespace Autobahn.Api.Contracts;
/// <summary>One individual occurrence, as shown in a group's drill-down.</summary>
public sealed class AdminErrorOccurrenceDto
{
    public required string Id { get; init; }
    public required string Fingerprint { get; init; }
    public DateTimeOffset TimestampUtc { get; init; }
    public required string Level { get; init; }
    public required string Source { get; init; }
    public required string Category { get; init; }
    public required string Message { get; init; }
    public string? ExceptionType { get; init; }
    public string? StackTrace { get; init; }
    public string? EndpointMethod { get; init; }
    public string? EndpointPath { get; init; }
    public int StatusCode { get; init; }
    public string? ErrorCode { get; init; }
    public string? Outcome { get; init; }
    public string? ModelId { get; init; }
    public string? UpstreamTarget { get; init; }
    public string? TenantId { get; init; }
    public string? ApiKeyId { get; init; }
    public string? RequestId { get; init; }
    public double? DurationMs { get; init; }
    public string? UpstreamBodySnippet { get; init; }
    public string? Hint { get; init; }
    public bool? IsStreaming { get; init; }
    public double? TimeToFirstTokenMs { get; init; }
    public long? ResponseBytesForwarded { get; init; }

    public static AdminErrorOccurrenceDto From(GatewayErrorRecord record) => new()
    {
        Id = record.Id,
        Fingerprint = record.Fingerprint,
        TimestampUtc = record.OccurredAt,
        Level = record.Level,
        Source = record.Source,
        Category = record.Category,
        Message = record.Message,
        ExceptionType = record.ExceptionType,
        StackTrace = record.StackTrace,
        EndpointMethod = record.Method,
        EndpointPath = record.Path,
        StatusCode = record.StatusCode,
        ErrorCode = record.EventCode,
        Outcome = record.Outcome,
        ModelId = record.ModelId,
        UpstreamTarget = record.UpstreamTarget,
        TenantId = record.TenantId,
        ApiKeyId = record.ApiKeyId,
        RequestId = record.RequestId,
        DurationMs = record.DurationMs,
        UpstreamBodySnippet = record.UpstreamBodySnippet,
        Hint = record.Hint,
        IsStreaming = record.IsStreaming,
        TimeToFirstTokenMs = record.TimeToFirstTokenMs,
        ResponseBytesForwarded = record.ResponseBytesForwarded,
    };
}
