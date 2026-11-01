using Autobahn.Domain.Models;

namespace Autobahn.Domain.Identity;
public static class ModelAccessRefusalExtensions
{
    /// <summary>The reason as callers, logs and the console spell it; null for an allowed request.</summary>
    public static string? ToReason(this ModelAccessRefusal refusal) => refusal switch
    {
        ModelAccessRefusal.None => null,
        ModelAccessRefusal.ProviderClosedForTenant => "provider_closed_for_tenant",
        ModelAccessRefusal.ProviderClosedForKey => "provider_closed_for_key",
        ModelAccessRefusal.ModelNotOnTenantList => "model_not_on_tenant_list",
        ModelAccessRefusal.ModelNotOnKeyList => "model_not_on_key_list",
        _ => throw new ArgumentOutOfRangeException(nameof(refusal), refusal, "Unknown refusal."),
    };
    /// <summary>One sentence a caller can act on.</summary>
    public static string Describe(this ModelAccessDecision decision, string modelId)
    {
        var provider = ModelProviders.DisplayName(decision.Provider);
        return decision.Refusal switch
        {
            ModelAccessRefusal.None => $"The API key may call '{modelId}'.",
            ModelAccessRefusal.ProviderClosedForTenant => $"'{modelId}' is served by {provider}, and this tenant is not opened for {provider}. An operator can open it.",
            ModelAccessRefusal.ProviderClosedForKey => $"'{modelId}' is served by {provider}, and this API key is not opened for {provider}. A tenant admin can open it.",
            ModelAccessRefusal.ModelNotOnTenantList => $"'{modelId}' is not on the tenant's model list.",
            ModelAccessRefusal.ModelNotOnKeyList => decision.Rule is null ? $"'{modelId}' is not on the API key's model list." : $"'{modelId}' is denied to the API key by the entry '{decision.Rule}'.",
            _ => throw new ArgumentOutOfRangeException(nameof(decision), decision.Refusal, "Unknown refusal."),
        };
    }
}
