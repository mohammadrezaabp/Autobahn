namespace Autobahn.Domain.Billing;
/// <summary>What a reprice found, and what it changed when it was not a dry run.</summary>
public sealed class BillingRepriceReport
{
    public required string ModelId { get; init; }
    public DateOnly From { get; init; }
    public DateOnly To { get; init; }
    public bool DryRun { get; init; }
    /// <summary>Whether the new costs were written.</summary>
    public bool Applied { get; init; }
    public required RateCardRecord RateCard { get; init; }
    /// <summary>Ledger rows for the model in the range.</summary>
    public int Events { get; init; }
    /// <summary>Rows whose cost changes.</summary>
    public int Repriced { get; init; }
    /// <summary>Rows left alone because the upstream reported what it charged; that figure is the invoice.</summary>
    public int SkippedReported { get; init; }
    /// <summary>Rows with no token split to price (an upstream that reported only a total, or nothing).</summary>
    public int SkippedNoTokens { get; init; }
    /// <summary>Repriced rows that ran in the serving provider's peak hours, priced at its peak rate.</summary>
    public int PeakEvents { get; init; }
    public long PromptTokens { get; init; }
    public long CachedPromptTokens { get; init; }
    public long CompletionTokens { get; init; }
    /// <summary>Stored cost of the rows in the range before the reprice.</summary>
    public decimal OldTotalUsd { get; init; }
    /// <summary>Their cost at the current rate card (rows that were skipped keep their stored cost).</summary>
    public decimal NewTotalUsd { get; init; }
    public required IReadOnlyList<BillingRepriceDay> Daily { get; init; }
    public required IReadOnlyList<BillingRepriceTenant> ByTenant { get; init; }
}
