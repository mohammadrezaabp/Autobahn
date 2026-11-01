using Autobahn.Domain.Configuration;
using Autobahn.Domain.RateLimiting;

namespace Autobahn.Api.Contracts;
/// <summary>
/// POST body for <c>/admin/api/rate-limits/schedule/preview</c>: a candidate rule set the console
/// has staged but not saved, and the same instant/range parameters the GET takes as query string.
/// </summary>
/// <remarks>
/// The GET answers for what is stored, which is the wrong question while an operator is composing a
/// change — the calendar they are checking is the one they are about to save, not the one already
/// deployed. Nothing here is persisted; the rules travel in the body because a staged set is far
/// too large for a query string.
/// </remarks>
public sealed class AdminRateLimitSchedulePreviewDto
{
    /// <summary>The staged rule set, complete. Unlike the PUT, null is not "leave the stored ones alone" — there is nothing to leave alone in a preview, so it reads as an empty set.</summary>
    public List<AdminRateLimitRuleDto>? Rules { get; set; }
    public DateTimeOffset? At { get; set; }
    /// <summary>The same wall-clock spelling the GET accepts, resolved in <c>timeZone</c>.</summary>
    public string? AtLocal { get; set; }
    public string? TimeZone { get; set; }
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
    public int? Take { get; set; }
}
