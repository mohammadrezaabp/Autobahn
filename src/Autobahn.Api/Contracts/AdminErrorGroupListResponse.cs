using Autobahn.Domain.Models;

namespace Autobahn.Api.Contracts;
public sealed class AdminErrorGroupListResponse
{
    public required IReadOnlyList<AdminErrorGroupDto> Groups { get; init; }
    /// <summary>Distinct groups matching the filter, before paging.</summary>
    public long Total { get; init; }
    /// <summary>Occurrences across all matching groups.</summary>
    public long OccurrenceTotal { get; init; }
    /// <summary>
    /// Occurrences the store holds in total, ignoring every filter and the time window — what lets
    /// an empty grid say whether the window is hiding rows or nothing was ever captured.
    /// </summary>
    public long StoredTotal { get; init; }
    /// <summary>Records dropped before persistence because the write buffer was full.</summary>
    public long DroppedTotal { get; init; }
    /// <summary>Records that failed to persist after a retry.</summary>
    public long PersistFailedTotal { get; init; }
    /// <summary>Records deleted by retention since the last clear.</summary>
    public long PrunedTotal { get; init; }
    /// <summary>Age cutoff of the last retention pass; nothing older is stored.</summary>
    public DateTimeOffset? RetainedSinceUtc { get; init; }
    /// <summary>True when the archive was unreachable and this page came from the in-memory buffer.</summary>
    public bool Degraded { get; init; }
    public int Limit { get; init; }
    public int Offset { get; init; }
    /// <summary><c>database</c> or <c>memory</c>.</summary>
    public required string Source { get; init; }
    /// <summary>
    /// False when the rows in this response are held in memory only and will not survive a restart.
    /// </summary>
    /// <remarks>
    /// Derived from where the page was actually served from, not from how the store is configured.
    /// A database-backed store that fell back to its in-memory buffer after a failed query is
    /// serving volatile data, and saying otherwise would put "kept across restarts" above rows that
    /// are not.
    /// </remarks>
    public bool Persisted { get; init; }

    public static AdminErrorGroupListResponse From(GatewayErrorGroupPage page, bool persisted) => new()
    {
        Groups = [..page.Items.Select(AdminErrorGroupDto.From)],
        Total = page.Total,
        OccurrenceTotal = page.OccurrenceTotal,
        StoredTotal = page.StoredTotal,
        DroppedTotal = page.DroppedTotal,
        PersistFailedTotal = page.PersistFailedTotal,
        PrunedTotal = page.PrunedTotal,
        RetainedSinceUtc = page.RetainedSinceUtc,
        Degraded = page.Degraded,
        Limit = page.Limit,
        Offset = page.Offset,
        Source = page.Source,
        Persisted = persisted && page.Source == GatewayErrorSources.Database,
    };
}
