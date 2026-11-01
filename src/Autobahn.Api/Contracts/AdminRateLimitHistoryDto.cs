using System.Text.Json;
using Autobahn.Application.Abstractions;

namespace Autobahn.Api.Contracts;
/// <summary>
/// One page of rate-limit history, read from the admin audit trail. Newest first.
/// </summary>
public sealed class AdminRateLimitHistoryDto
{
    /// <summary>False when no audit trail exists to read; <see cref = "Entries"/> is then empty, which is not "no changes".</summary>
    public bool Available { get; set; }
    public IReadOnlyList<AdminRateLimitHistoryEntryDto> Entries { get; set; } = [];
    /// <summary>Whether an older matching entry exists.</summary>
    public bool HasMore { get; set; }
    /// <summary>
    /// Pass back unchanged as <c>before</c> for the next page; null on the last page. Opaque: it
    /// carries a position, not just a timestamp, so a page boundary inside a group of records
    /// written in the same tick neither loses them nor repeats them.
    /// </summary>
    public string? NextBefore { get; set; }
    /// <summary>
    /// The read stopped at its scan ceiling before the end of the trail: older entries may exist
    /// that were not reached. The trail is shared with every other admin action.
    /// </summary>
    public bool ScanLimitReached { get; set; }
}
