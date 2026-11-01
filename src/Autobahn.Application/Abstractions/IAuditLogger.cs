namespace Autobahn.Application.Abstractions;
public interface IAuditLogger
{
    void LogAdminAction(string action, AuditLogEntry entry);
}
