using Autobahn.Domain.Models;

namespace Autobahn.Api.Contracts;
public sealed class AdminErrorClearResponse
{
    public bool Success { get; init; }
    public required string Message { get; init; }
    public int RecordsDeleted { get; init; }
    /// <summary>False when the stored records could not be deleted; the counters were reset regardless.</summary>
    public bool ArchiveCleared { get; init; }
    public int RecentRequestRowsRemoved { get; init; }
    public long TotalErrorsCleared { get; init; }
    /// <summary>
    /// False means the persisted counter snapshot was left alone, so a restart would restore the
    /// old totals. Surfaced rather than hidden: a clear that silently does not stick is worse than
    /// one that reports its limits.
    /// </summary>
    public bool SnapshotRewritten { get; init; }
    public bool DatabaseAvailable { get; init; }

    public static AdminErrorClearResponse From(GatewayErrorClearResult result) => new()
    {
        Success = result.ArchiveCleared,
        Message = !result.ArchiveCleared ? "Error counters were reset, but the stored error records could not be deleted. Retry once the database is reachable." : result.DatabaseAvailable ? "Errors cleared and the persisted counter snapshot was rewritten." : "Errors cleared from memory. No database is configured, so nothing was persisted.",
        RecordsDeleted = result.RecordsDeleted,
        ArchiveCleared = result.ArchiveCleared,
        RecentRequestRowsRemoved = result.RecentRequestRowsRemoved,
        TotalErrorsCleared = result.TotalErrorsCleared,
        SnapshotRewritten = result.SnapshotRewritten,
        DatabaseAvailable = result.DatabaseAvailable,
    };
}
