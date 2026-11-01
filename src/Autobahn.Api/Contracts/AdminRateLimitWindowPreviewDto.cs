using Autobahn.Domain.Configuration;
using Autobahn.Domain.RateLimiting;

namespace Autobahn.Api.Contracts;
/// <summary>
/// POST body for <c>/admin/api/rate-limits/windows/preview</c>: the rule as it would be with the
/// candidate window included, and which window is being composed.
/// </summary>
public sealed class AdminRateLimitWindowPreviewDto
{
    public string Scope { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public int Rpm { get; set; }
    public int Burst { get; set; }
    public int MaxConcurrentStreams { get; set; }
    public int Tpm { get; set; }
    public List<AdminRateLimitWindowDto> Windows { get; set; } = [];
    public string Candidate { get; set; } = string.Empty;
}
