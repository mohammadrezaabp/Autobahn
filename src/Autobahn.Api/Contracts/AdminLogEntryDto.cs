using Autobahn.Domain.Models;

namespace Autobahn.Api.Contracts;
/// <summary>Wire shape for one row of the admin Logs tab.</summary>
public sealed class AdminLogEntryDto
{
    public required string Id { get; init; }
    public DateTimeOffset TimestampUtc { get; init; }
    public DateTimeOffset LastTimestampUtc { get; init; }
    public required string Level { get; init; }
    public required string Category { get; init; }
    public string? EventCode { get; init; }
    public required string Message { get; init; }
    public string? Detail { get; init; }
    public string? Hint { get; init; }
    public string? ModelId { get; init; }
    public string? RequestId { get; init; }
    public int Repeats { get; init; }

    public static AdminLogEntryDto From(GatewayLogEntry entry) => new()
    {
        Id = entry.Id,
        TimestampUtc = entry.TimestampUtc,
        LastTimestampUtc = entry.LastTimestampUtc == default ? entry.TimestampUtc : entry.LastTimestampUtc,
        Level = entry.Level,
        Category = entry.Category,
        EventCode = entry.EventCode,
        Message = entry.Message,
        Detail = entry.Detail,
        Hint = entry.Hint,
        ModelId = entry.ModelId,
        RequestId = entry.RequestId,
        Repeats = entry.Repeats < 1 ? 1 : entry.Repeats,
    };
}
