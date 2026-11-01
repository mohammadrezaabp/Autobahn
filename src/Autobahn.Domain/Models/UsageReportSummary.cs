using Autobahn.Domain.Billing;

namespace Autobahn.Domain.Models;
public sealed class UsageReportSummary
{
    public long TotalPromptTokens { get; init; }
    public long TotalCompletionTokens { get; init; }
    public decimal TotalCost { get; init; }
    public int TotalRequests { get; init; }
    /// <summary>Requests in the report that carry no tenant (anonymous public-model traffic).</summary>
    public int AnonymousRequests { get; init; }
}
