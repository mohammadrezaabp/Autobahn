namespace Autobahn.Domain.Models;
/// <summary>A page of individual error occurrences.</summary>
public sealed record GatewayErrorPage
{
    public IReadOnlyList<GatewayErrorRecord> Items { get; init; } = [];
    /// <summary>
    /// Rows matching the filter <em>before</em> <see cref = "Limit"/> is applied. The Logs tab's
    /// truncation hint is wrong today precisely because its equivalent field is the page length.
    /// </summary>
    public long Total { get; init; }
    public int Limit { get; init; }
    public int Offset { get; init; }
    public string Source { get; init; } = GatewayErrorSources.Memory;
    /// <summary>True when a database-backed store served this page from its in-memory buffer.</summary>
    public bool Degraded { get; init; }
}
