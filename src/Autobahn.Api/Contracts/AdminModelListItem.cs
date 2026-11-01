using Autobahn.Domain.Billing;
using Autobahn.Domain.Models;

namespace Autobahn.Api.Contracts;

public sealed class AdminModelListItem
{
    public required ModelConfig Model { get; init; }

    public bool HasUpstreamCredential { get; init; }

    /// <summary>
    /// Who serves the model, as the access rules read it: <c>Model.Provider</c> when an operator set
    /// one, otherwise what the backend addresses say.
    /// </summary>
    public string Provider { get; init; } = ModelProviders.SelfHosted;

    /// <summary>Current per-million-token price, or null when the model is unpriced.</summary>
    public ModelPricing? Pricing { get; init; }
}
