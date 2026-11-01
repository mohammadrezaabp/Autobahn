namespace Autobahn.Domain.Models;
public sealed class UsageKeyShare
{
    /// <summary><see langword="null"/> for requests sent without an API key (anonymous public-model traffic).</summary>
    public Guid? ApiKeyId { get; init; }
    public string? KeyPrefix { get; init; }
    public string? Label { get; init; }
    public string? Assignee { get; init; }
    public int Requests { get; init; }
    public long PromptTokens { get; init; }
    public long CompletionTokens { get; init; }
    public decimal TotalCost { get; init; }
    /// <summary>Fraction (0–1) of <see cref = "UsageKeySharesResponse.TotalRequests"/>.</summary>
    public double RequestShare { get; init; }
    /// <summary>Fraction (0–1) of <see cref = "UsageKeySharesResponse.TotalTokens"/>; 0 when no tokens were recorded.</summary>
    public double TokenShare { get; init; }
    /// <summary>Fraction (0–1) of <see cref = "UsageKeySharesResponse.TotalCost"/>; <see langword="null"/> when nothing was priced.</summary>
    public double? CostShare { get; init; }
}
