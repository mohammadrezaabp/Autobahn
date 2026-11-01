using Autobahn.Domain.Models;

namespace Autobahn.Api.Contracts;
public sealed class AdminLogListResponse
{
    public required IReadOnlyList<AdminLogEntryDto> Entries { get; init; }
    /// <summary>Rows returned after filtering — not the buffer's total size.</summary>
    public int Count { get; init; }
    /// <summary>
    /// Rows matching the filter before <c>limit</c> was applied. Without this the UI cannot
    /// distinguish "200 of 741 matches" from "200 of 200", and its truncation warning fires
    /// whenever a page happens to be exactly full.
    /// </summary>
    public int Total { get; init; }
    /// <summary>Ring-buffer capacity, so the UI can say what the operator is not seeing.</summary>
    public int Capacity { get; init; }
}
