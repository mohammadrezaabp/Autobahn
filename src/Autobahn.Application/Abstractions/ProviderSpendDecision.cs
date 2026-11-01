namespace Autobahn.Application.Abstractions;
/// <summary>The answer to "may this key spend more at this provider this month?".</summary>
/// <param name = "CapUsd">The cap that refused; null when allowed.</param>
/// <param name = "SpentUsd">What that scope has spent this month, in-flight requests included.</param>
public sealed record ProviderSpendDecision(bool IsAllowed, ProviderSpendScope Scope = ProviderSpendScope.None, decimal? CapUsd = null, decimal SpentUsd = 0m)
{
    public static ProviderSpendDecision Allowed { get; } = new(true);
}
