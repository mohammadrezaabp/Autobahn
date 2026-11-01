using Autobahn.Domain.Models;

namespace Autobahn.Domain.Identity;

public static class ModelGrantEvaluator
{
    public static bool IsModelAllowed(IReadOnlyList<ModelGrantRecord> grants, string canonicalModelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalModelId);

        if (grants.Count == 0)
        {
            return true;
        }

        return Match(grants.Select(g => (g.Effect, g.ModelPattern)), canonicalModelId, ModelProviders.SelfHosted)
            .Allowed;
    }

    public static bool IsModelAllowed(
        IReadOnlyList<ModelGrantRecord> tenantGrants,
        IReadOnlyList<ApiKeyModelGrantRecord> apiKeyGrants,
        string canonicalModelId) =>
        Evaluate(tenantGrants, apiKeyGrants, [], apiKeyId: null, canonicalModelId, ModelProviders.SelfHosted)
            .IsAllowed;

    /// <summary>
    /// Decides whether a key may call a model, stopping at the first rule that refuses.
    /// </summary>
    /// <remarks>
    /// <para>The order is the one a caller is told about: the provider switch for the tenant, the
    /// provider switch for the key, the tenant's model list, the key's model list. The two switches
    /// exist only for a paid provider, where they are closed until someone opens them; a
    /// self-hosted model starts at the lists, as it always did.</para>
    ///
    /// <para>An empty tenant list is no ceiling. It does not open a paid provider: that is what the
    /// tenant's switch is for, so registering a paid model opens it to nobody.</para>
    /// </remarks>
    /// <param name="providerAccess">The tenant's provider switches, its own and its keys'.</param>
    /// <param name="apiKeyId">The key whose switch is read; null skips the switches.</param>
    public static ModelAccessDecision Evaluate(
        IReadOnlyList<ModelGrantRecord> tenantGrants,
        IReadOnlyList<ApiKeyModelGrantRecord> apiKeyGrants,
        IReadOnlyList<ProviderAccessRecord> providerAccess,
        Guid? apiKeyId,
        string canonicalModelId,
        string provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalModelId);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);

        if (ModelProviders.IsPaid(provider) && apiKeyId is not null)
        {
            if (!IsProviderOpen(providerAccess, null, provider))
            {
                return new ModelAccessDecision(ModelAccessRefusal.ProviderClosedForTenant, provider);
            }

            if (!IsProviderOpen(providerAccess, apiKeyId, provider))
            {
                return new ModelAccessDecision(ModelAccessRefusal.ProviderClosedForKey, provider);
            }
        }

        if (tenantGrants.Count > 0)
        {
            var tenant = Match(tenantGrants.Select(g => (g.Effect, g.ModelPattern)), canonicalModelId, provider);
            if (!tenant.Allowed)
            {
                return new ModelAccessDecision(ModelAccessRefusal.ModelNotOnTenantList, provider, tenant.DeniedBy);
            }
        }

        var key = Match(apiKeyGrants.Select(g => (g.Effect, g.ModelPattern)), canonicalModelId, provider);
        return key.Allowed
            ? ModelAccessDecision.Allowed(provider, key.AllowedBy)
            : new ModelAccessDecision(ModelAccessRefusal.ModelNotOnKeyList, provider, key.DeniedBy);
    }

    /// <summary>Whether <paramref name="provider"/> is open for the tenant (<paramref name="apiKeyId"/> null) or for a key.</summary>
    public static bool IsProviderOpen(
        IReadOnlyList<ProviderAccessRecord> providerAccess,
        Guid? apiKeyId,
        string provider)
    {
        if (!ModelProviders.IsPaid(provider))
        {
            return true;
        }

        foreach (var record in providerAccess)
        {
            if (record.ApiKeyId == apiKeyId &&
                string.Equals(record.Provider, provider, StringComparison.OrdinalIgnoreCase))
            {
                return record.IsOpen;
            }
        }

        return false;
    }

    /// <summary>
    /// Evaluates one grant list. A <see cref="GrantEffect.Deny"/> that matches the model wins over
    /// any <see cref="GrantEffect.Allow"/>, which is what lets one model be held back from a key a
    /// prefix would otherwise give it.
    /// </summary>
    /// <remarks>
    /// Deny used to be inert: only Allow entries were examined, so a Deny alongside an Allow for the
    /// same model changed nothing. Deny-wins is the only safe reading of an authorization rule.
    /// </remarks>
    private static (bool Allowed, string? AllowedBy, string? DeniedBy) Match(
        IEnumerable<(GrantEffect Effect, string ModelPattern)> grants,
        string canonicalModelId,
        string provider)
    {
        string? allowedBy = null;

        foreach (var (effect, pattern) in grants)
        {
            if (!ModelGrantPattern.Matches(pattern, canonicalModelId, provider))
            {
                continue;
            }

            if (effect == GrantEffect.Deny)
            {
                return (false, null, pattern);
            }

            allowedBy ??= pattern;
        }

        return (allowedBy is not null, allowedBy, null);
    }
}
