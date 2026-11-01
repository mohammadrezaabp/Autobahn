namespace Autobahn.Application.Abstractions;
public sealed record AuditLogEntry(string? TenantId, string? ApiKeyId, object? Details = null);
