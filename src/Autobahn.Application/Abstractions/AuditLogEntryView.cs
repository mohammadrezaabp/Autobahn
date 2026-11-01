namespace Autobahn.Application.Abstractions;
/// <param name = "Details">The action's details re-serialised as compact JSON; null when the record had none.</param>
public sealed record AuditLogEntryView(DateTimeOffset TimestampUtc, string Action, string? TenantId, string? ApiKeyId, string? Details);
