namespace Autobahn.Application.Abstractions;
public sealed record AuditLogReadResult(IReadOnlyList<AuditLogEntryView> Entries, int ParseErrors, DateTimeOffset? NewestUtc)
{
    /// <summary>Whether at least one older matching record exists beyond the ones returned.</summary>
    public bool HasMore { get; init; }
    /// <summary>
    /// Whether reading stopped at the scan ceiling rather than at the end of the trail, so older
    /// matches may exist that this call did not reach.
    /// </summary>
    public bool ScanLimitReached { get; init; }
    /// <summary>Where to resume for the next page; null when <see cref = "HasMore"/> is false.</summary>
    public AuditLogCursor? NextCursor { get; init; }
}
