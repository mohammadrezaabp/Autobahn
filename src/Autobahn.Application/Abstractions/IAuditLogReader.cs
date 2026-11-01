namespace Autobahn.Application.Abstractions;
/// <summary>Reads the admin audit trail back, newest first, for the Overview's activity card.</summary>
public interface IAuditLogReader
{
    /// <summary>True when a trail exists to read (the file has been created).</summary>
    bool IsAvailable { get; }

    Task<AuditLogReadResult> ReadRecentAsync(int limit, CancellationToken cancellationToken = default);
    /// <summary>
    /// The newest records matching <paramref name = "query"/>. Bounded twice: by how many are returned
    /// and by how many are read to find them, so a filter that matches nothing cannot turn one
    /// request into a scan of the whole trail.
    /// </summary>
    Task<AuditLogReadResult> ReadRecentAsync(AuditLogQuery query, CancellationToken cancellationToken = default);
}
