using Autobahn.Application.Abstractions;
using Autobahn.Domain.Identity;
using Autobahn.Domain.Models;

namespace Autobahn.Security.Services;

/// <summary>
/// Opens and closes paid providers for a tenant and its keys, and answers what the access rules
/// would decide for one key and one model.
/// </summary>
public sealed class ProviderAccessAdminService : IProviderAccessAdminService
{
    private readonly IProviderAccessRepository _providerAccess;
    private readonly IModelGrantRepository _tenantGrants;
    private readonly IApiKeyModelGrantRepository _apiKeyGrants;
    private readonly IApiKeyRepository _apiKeys;
    private readonly IModelRegistry _registry;
    private readonly IModelGrantService _grantService;
    private readonly IProviderSpendCapService? _spendCaps;

    /// <summary>The largest cap accepted; beyond it the figure is a typing mistake, not a cap.</summary>
    public const decimal MaxMonthlyCapUsd = 10_000_000m;

    public ProviderAccessAdminService(
        IProviderAccessRepository providerAccess,
        IModelGrantRepository tenantGrants,
        IApiKeyModelGrantRepository apiKeyGrants,
        IApiKeyRepository apiKeys,
        IModelRegistry registry,
        IModelGrantService grantService,
        // Optional: absent where billing is not composed, and then nothing is capped or spent.
        IProviderSpendCapService? spendCaps = null)
    {
        _spendCaps = spendCaps;
        _providerAccess = providerAccess;
        _tenantGrants = tenantGrants;
        _apiKeyGrants = apiKeyGrants;
        _apiKeys = apiKeys;
        _registry = registry;
        _grantService = grantService;
    }

    public async Task<ProviderAccessResponse> GetTenantAccessAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var access = await _providerAccess.ListByTenantAsync(tenantId, cancellationToken).ConfigureAwait(false);
        return await DescribeAsync(tenantId, access, apiKeyId: null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProviderCapChange> SetTenantCapAsync(
        Guid tenantId,
        string provider,
        decimal? monthlyCapUsd,
        CancellationToken cancellationToken = default)
    {
        var paid = RequirePaidProvider(provider);
        var cap = NormalizeCap(monthlyCapUsd);
        var previous = await _providerAccess.SetCapAsync(tenantId, null, paid, cap, cancellationToken)
            .ConfigureAwait(false);
        Invalidate(tenantId, paid);
        return new ProviderCapChange(paid, cap, previous);
    }

    public async Task<ProviderCapChange> SetApiKeyCapAsync(
        Guid tenantId,
        Guid apiKeyId,
        string provider,
        decimal? monthlyCapUsd,
        CancellationToken cancellationToken = default)
    {
        var paid = RequirePaidProvider(provider);
        await RequireInferenceKeyAsync(tenantId, apiKeyId, cancellationToken).ConfigureAwait(false);
        var cap = NormalizeCap(monthlyCapUsd);

        if (cap is { } keyCap)
        {
            // A key cap above the tenant's would never be reached, and would read as a promise the
            // tenant's cap then breaks.
            var access = await _providerAccess.ListByTenantAsync(tenantId, cancellationToken).ConfigureAwait(false);
            var tenantCap = CapOf(access, null, paid);
            if (tenantCap is { } ceiling && keyCap > ceiling)
            {
                throw new ArgumentException(
                    $"The key's cap ({keyCap:0.##} USD) cannot exceed the tenant's {ModelProviders.DisplayName(paid)} cap "
                    + $"({ceiling:0.##} USD).",
                    nameof(monthlyCapUsd));
            }
        }

        var previous = await _providerAccess.SetCapAsync(tenantId, apiKeyId, paid, cap, cancellationToken)
            .ConfigureAwait(false);
        Invalidate(tenantId, paid);
        return new ProviderCapChange(paid, cap, previous);
    }

    private void Invalidate(Guid tenantId, string provider)
    {
        _grantService.InvalidateProviderAccess(tenantId);
        _spendCaps?.Invalidate(tenantId, provider);
    }

    private static decimal? NormalizeCap(decimal? monthlyCapUsd)
    {
        if (monthlyCapUsd is not { } cap)
        {
            return null;
        }

        if (cap < 0m || cap > MaxMonthlyCapUsd)
        {
            throw new ArgumentException(
                $"monthlyCapUsd must be between 0 and {MaxMonthlyCapUsd:0}.",
                nameof(monthlyCapUsd));
        }

        return decimal.Round(cap, 2, MidpointRounding.AwayFromZero);
    }

    private static decimal? CapOf(IReadOnlyList<ProviderAccessRecord> access, Guid? apiKeyId, string provider) =>
        access.FirstOrDefault(a =>
            a.ApiKeyId == apiKeyId &&
            string.Equals(a.Provider, provider, StringComparison.OrdinalIgnoreCase))?.MonthlyCapUsd;

    public async Task<ProviderAccessChange> SetTenantAccessAsync(
        Guid tenantId,
        string provider,
        bool open,
        CancellationToken cancellationToken = default)
    {
        var paid = RequirePaidProvider(provider);
        var previous = await _providerAccess.SetAsync(tenantId, null, paid, open, cancellationToken)
            .ConfigureAwait(false);
        _grantService.InvalidateProviderAccess(tenantId);
        return new ProviderAccessChange(paid, open, previous);
    }

    public async Task<ProviderAccessResponse> GetApiKeyAccessAsync(
        Guid tenantId,
        Guid apiKeyId,
        CancellationToken cancellationToken = default)
    {
        await RequireInferenceKeyAsync(tenantId, apiKeyId, cancellationToken).ConfigureAwait(false);
        var access = await _providerAccess.ListByTenantAsync(tenantId, cancellationToken).ConfigureAwait(false);
        return await DescribeAsync(tenantId, access, apiKeyId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProviderAccessChange> SetApiKeyAccessAsync(
        Guid tenantId,
        Guid apiKeyId,
        string provider,
        bool open,
        CancellationToken cancellationToken = default)
    {
        var paid = RequirePaidProvider(provider);
        await RequireInferenceKeyAsync(tenantId, apiKeyId, cancellationToken).ConfigureAwait(false);

        // Closing is always allowed. Opening a key under a closed tenant would do nothing now and
        // take effect by surprise the day an operator opens the tenant.
        if (open)
        {
            var access = await _providerAccess.ListByTenantAsync(tenantId, cancellationToken).ConfigureAwait(false);
            if (!ModelGrantEvaluator.IsProviderOpen(access, null, paid))
            {
                throw new InvalidOperationException(
                    $"The tenant is not opened for {ModelProviders.DisplayName(paid)}. An operator has to open "
                    + "the tenant before one of its keys can be opened.");
            }
        }

        var previous = await _providerAccess.SetAsync(tenantId, apiKeyId, paid, open, cancellationToken)
            .ConfigureAwait(false);
        _grantService.InvalidateProviderAccess(tenantId);
        return new ProviderAccessChange(paid, open, previous);
    }

    /// <remarks>
    /// Read from the database, not through the request path's cache, so the answer is the one a
    /// change made a moment ago will produce.
    /// </remarks>
    public async Task<AccessCheckResponse> CheckAsync(
        Guid tenantId,
        Guid apiKeyId,
        string model,
        CancellationToken cancellationToken = default)
    {
        await RequireInferenceKeyAsync(tenantId, apiKeyId, cancellationToken).ConfigureAwait(false);

        var name = model?.Trim() ?? string.Empty;
        if (name.Length == 0 || !_registry.TryGetModel(name, out var config) || config is null)
        {
            return new AccessCheckResponse
            {
                Allowed = false,
                Reason = "model_not_found",
                Message = $"No model named '{name}' is registered.",
            };
        }

        var provider = ModelProviders.Resolve(config);
        var decision = ModelGrantEvaluator.Evaluate(
            await _tenantGrants.ListByTenantAsync(tenantId, cancellationToken).ConfigureAwait(false),
            await _apiKeyGrants.ListByApiKeyAsync(apiKeyId, cancellationToken).ConfigureAwait(false),
            await _providerAccess.ListByTenantAsync(tenantId, cancellationToken).ConfigureAwait(false),
            apiKeyId,
            config.Id,
            provider);

        // The rules can pass a model the gateway still will not serve; saying "allowed" for it
        // would send the operator looking in the wrong place.
        var stopped = decision.IsAllowed && config.IsStopped();
        return new AccessCheckResponse
        {
            Allowed = decision.IsAllowed && !stopped,
            Reason = stopped ? "model_stopped" : decision.Refusal.ToReason(),
            Message = stopped
                ? $"The API key may call '{config.Id}', but the model has been stopped by an administrator."
                : decision.Describe(config.Id),
            ModelId = config.Id,
            Provider = provider,
            Rule = decision.Rule,
        };
    }

    private async Task<ProviderAccessResponse> DescribeAsync(
        Guid tenantId,
        IReadOnlyList<ProviderAccessRecord> access,
        Guid? apiKeyId,
        CancellationToken cancellationToken)
    {
        var items = new List<ProviderAccessItem>(ModelProviders.All.Count);
        foreach (var provider in ModelProviders.All)
        {
            var paid = ModelProviders.IsPaid(provider);

            // Spend is only read for a provider the tenant has ever been given a row for; the
            // others have none to show.
            var touched = paid && access.Any(a => string.Equals(a.Provider, provider, StringComparison.OrdinalIgnoreCase));
            var spend = touched && _spendCaps is not null
                ? await _spendCaps.GetMonthSpendAsync(tenantId, provider, cancellationToken).ConfigureAwait(false)
                : ProviderMonthSpend.Empty;

            items.Add(new ProviderAccessItem
            {
                Provider = provider,
                DisplayName = ModelProviders.DisplayName(provider),
                Paid = paid,
                Open = ModelGrantEvaluator.IsProviderOpen(access, apiKeyId, provider),
                TenantOpen = ModelGrantEvaluator.IsProviderOpen(access, null, provider),
                OpenKeyCount = access.Count(a =>
                    a.ApiKeyId is not null && a.IsOpen &&
                    string.Equals(a.Provider, provider, StringComparison.OrdinalIgnoreCase)),
                MonthlyCapUsd = CapOf(access, apiKeyId, provider),
                SpentThisMonthUsd = apiKeyId is { } keyId ? spend.ForKey(keyId) : spend.TenantTotal,
                TenantMonthlyCapUsd = CapOf(access, null, provider),
                TenantSpentThisMonthUsd = spend.TenantTotal,
                Forwardable = !ModelProviders.TryGetPaid(provider, out var definition) || definition.Forwardable,
            });
        }

        return new ProviderAccessResponse { Providers = items };
    }

    private static string RequirePaidProvider(string provider)
    {
        if (!ModelProviders.TryNormalize(provider, out var normalized, out var error) || normalized is null)
        {
            throw new ArgumentException(error ?? "A provider is required.", nameof(provider));
        }

        if (!ModelProviders.IsPaid(normalized))
        {
            throw new ArgumentException(
                "Self-hosted models have no switch: a key reaches them through its model list alone.",
                nameof(provider));
        }

        return normalized;
    }

    private async Task RequireInferenceKeyAsync(Guid tenantId, Guid apiKeyId, CancellationToken cancellationToken)
    {
        var key = await _apiKeys.GetByIdAsync(apiKeyId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"API key '{apiKeyId}' was not found.");

        if (key.TenantId != tenantId)
        {
            throw new UnauthorizedAccessException("API key does not belong to the current tenant.");
        }

        if (key.Role is not ApiKeyRole.Inference and not ApiKeyRole.Both)
        {
            throw new InvalidOperationException("Provider access applies only to inference API keys.");
        }
    }
}
