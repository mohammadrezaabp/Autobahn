using Autobahn.Domain.Billing;

namespace Autobahn.Application.Abstractions;

public interface IRateCardCostCalculator
{
    BillingCostBreakdown Calculate(RateCardRecord rateCard, long promptTokens, long completionTokens);

    /// <summary>
    /// As above, pricing <paramref name="cachedPromptTokens"/> of the prompt at the card's cached
    /// rate. The card's per-request price is added to the input side.
    /// </summary>
    BillingCostBreakdown Calculate(
        RateCardRecord rateCard,
        long promptTokens,
        long completionTokens,
        long cachedPromptTokens) =>
        Calculate(rateCard, promptTokens, completionTokens);

    /// <summary>
    /// Prices a response whose upstream reported only a combined token total, with no input/output
    /// split.
    /// </summary>
    /// <remarks>
    /// Priced at the higher of the card's input and output rates. That is the same conservative
    /// convention budget reservation already uses to bound an unknown cost, and it errs toward
    /// over- rather than under-charging — the previous behaviour attributed the whole total to input
    /// tokens, which under-billed every such model by the input/output rate ratio.
    /// </remarks>
    BillingCostBreakdown CalculateFromTotalTokens(RateCardRecord rateCard, long totalTokens);
}
