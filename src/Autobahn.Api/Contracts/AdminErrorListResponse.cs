using Autobahn.Domain.Models;

namespace Autobahn.Api.Contracts;
public sealed class AdminErrorListResponse
{
    public required IReadOnlyList<AdminErrorOccurrenceDto> Occurrences { get; init; }
    /// <summary>Occurrences matching the filter, before paging.</summary>
    public long Total { get; init; }
    public int Limit { get; init; }
    public int Offset { get; init; }
    public required string Source { get; init; }
    public bool Persisted { get; init; }
    /// <summary>True when the archive was unreachable and this page came from the in-memory buffer.</summary>
    public bool Degraded { get; init; }

    public static AdminErrorListResponse From(GatewayErrorPage page, bool persisted) => new()
    {
        Occurrences = [..page.Items.Select(AdminErrorOccurrenceDto.From)],
        Total = page.Total,
        Limit = page.Limit,
        Offset = page.Offset,
        Source = page.Source,
        Persisted = persisted && page.Source == GatewayErrorSources.Database,
        Degraded = page.Degraded,
    };
}
