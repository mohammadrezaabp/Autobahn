namespace Autobahn.Domain.Billing;
public static class BillingCostSources
{
    /// <summary>The charge the upstream reported for the request.</summary>
    public const string Reported = "reported";
    /// <summary>Tokens times the model's price.</summary>
    public const string RateCard = "rate_card";
}
