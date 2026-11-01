using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Autobahn.Api.Contracts;
using Autobahn.Application.Abstractions;
using Autobahn.Domain.Billing;
using Autobahn.Domain.Identity;
using Autobahn.Domain.Models;
using Autobahn.Application.Providers;

namespace Autobahn.Api.Services;
/// <summary>
/// What the Providers page reads and writes: each paid provider's state, who may spend there and
/// how much, what was spent, the company key and the catalogue.
/// </summary>
public sealed class ProviderAdminService(IServiceProvider services, IModelRegistry registry, IProviderCredentialSource credentials, ProviderStatusStore status, OpenAiCompatibleProviderModelsClient catalogClient, AdminModelProvisioningService provisioning, ProviderStatusRefreshService refresher, IMemoryCache cache)
{
    private static readonly TimeSpan CatalogTtl = TimeSpan.FromMinutes(10);
    public const int MaxCatalogResults = 200;
    public const int MaxSpendDays = 366;
    public const int MaxModelsPerAdd = 50;
    public const decimal MaxLowBalanceUsd = 10_000_000m;
    public static ModelProviderDefinition RequireProvider(string provider) => ModelProviders.TryGetPaid(provider, out var definition) ? definition : throw new ProviderAdminException($"Unknown provider '{provider}'.", 404);
    private static ProviderPeakPricingDto? ToDto(PeakPricingPolicy? policy, string provider, DateOnly today) => policy?.Describe(provider)is not { } schedule ? null : new ProviderPeakPricingDto
    {
        Multiplier = schedule.Multiplier,
        EffectiveFromUtc = schedule.EffectiveFromUtc,
        // Monday first, as the week is read, rather than the enum's Sunday.
        Days = [..schedule.Days.OrderBy(d => ((int)d + 6) % 7).Select(d => d.ToString())],
        WindowsUtc = schedule.WindowsUtc,
        HolidayCalendar = schedule.HolidayCalendar,
        UpcomingExcludedDates = policy.UpcomingDaysOff(provider, today),
        YearsWithoutHolidays = policy.YearsWithoutHolidays(provider, today),
    };
    public async Task<ProviderListResponse> ListAsync(CancellationToken cancellationToken)
    {
        var settings = await ListSettingsAsync(cancellationToken).ConfigureAwait(false);
        var models = registry.GetAllModels();
        var(monthStart, today) = CurrentMonth();
        var spendRepository = services.GetService<IProviderSpendRepository>();
        var accessRepository = services.GetService<IProviderAccessRepository>();
        var peakPricing = services.GetService<PeakPricingPolicy>();
        var items = new List<ProviderOverviewItem>(ModelProviders.Paid.Count);
        foreach (var definition in ModelProviders.Paid)
        {
            var spend = spendRepository is null ? [] : await spendRepository.ListAsync(monthStart, today, definition.Id, null, cancellationToken).ConfigureAwait(false);
            var access = accessRepository is null ? [] : await accessRepository.ListByProviderAsync(definition.Id, cancellationToken).ConfigureAwait(false);
            var stored = settings.FirstOrDefault(s => string.Equals(s.Provider, definition.Id, StringComparison.OrdinalIgnoreCase));
            var origin = credentials.OriginOf(definition.Id);
            items.Add(new ProviderOverviewItem { Provider = definition.Id, DisplayName = definition.DisplayName, UpstreamBaseUrl = definition.UpstreamBaseUrl, EnvVar = definition.DefaultEnvVar, KeyConfigured = origin != ProviderCredentialOrigin.None, KeySource = origin switch
            {
                ProviderCredentialOrigin.Stored => "stored",
                ProviderCredentialOrigin.Environment => "environment",
                _ => "none",
            }, Forwardable = definition.Forwardable, SupportsCatalog = definition.CatalogUrl is not null, ReportsCost = definition.ReportsCost, SupportsBalance = definition.Balance != ProviderBalanceKind.None, BalanceEnvVar = definition.BalanceEnvVar, // A provider whose balance key is its own is read once the refresh service has
            // found that key; the company key's own origin answers for the rest.
            BalanceKeyConfigured = definition.BalanceEnvVar is null ? origin != ProviderCredentialOrigin.None : status.Watched.Contains(definition.Id, StringComparer.OrdinalIgnoreCase), Balance = ToDto(status.GetBalance(definition.Id)), LowBalanceUsd = stored?.LowBalanceUsd, NewModelsStartStopped = stored?.NewModelsStartStopped ?? false, ModelCount = models.Count(m => UsesProvider(m, definition.Id)), OpenTenantCount = access.Count(a => a.ApiKeyId is null && a.IsOpen), OpenKeyCount = access.Count(a => a.ApiKeyId is not null && a.IsOpen), SpentThisMonthUsd = spend.Sum(s => s.Cost), RequestsThisMonth = spend.Sum(s => s.RequestCount), PeakPricing = ToDto(peakPricing, definition.Id, today), });
        }

        return new ProviderListResponse
        {
            Providers = items
        };
    }

    public async Task<ProviderTenantAccessResponse> GetTenantAccessAsync(string provider, CancellationToken cancellationToken)
    {
        var definition = RequireProvider(provider);
        var tenants = Require<ITenantRepository>();
        var accessRepository = Require<IProviderAccessRepository>();
        var spendRepository = Require<IProviderSpendRepository>();
        var access = await accessRepository.ListByProviderAsync(definition.Id, cancellationToken).ConfigureAwait(false);
        var(monthStart, today) = CurrentMonth();
        var spent = (await spendRepository.ListAsync(monthStart, today, definition.Id, null, cancellationToken).ConfigureAwait(false)).Where(s => s.TenantId is not null).GroupBy(s => s.TenantId!.Value).ToDictionary(g => g.Key, g => g.Sum(s => s.Cost));
        var rows = (await tenants.ListAllAsync(cancellationToken).ConfigureAwait(false)).Where(t => t.IsActive).Select(t =>
        {
            var own = access.FirstOrDefault(a => a.TenantId == t.Id && a.ApiKeyId is null);
            return new ProviderTenantAccessRow
            {
                TenantId = t.Id,
                TenantSlug = t.Slug,
                TenantName = t.Name,
                Open = own?.IsOpen ?? false,
                MonthlyCapUsd = own?.MonthlyCapUsd,
                SpentThisMonthUsd = spent.TryGetValue(t.Id, out var cost) ? cost : 0m,
                OpenKeyCount = access.Count(a => a.TenantId == t.Id && a.ApiKeyId is not null && a.IsOpen),
            };
        })// Open tenants first, then by what they spent: the rows an operator came to look at.
        .OrderByDescending(r => r.Open).ThenByDescending(r => r.SpentThisMonthUsd).ThenBy(r => r.TenantSlug, StringComparer.OrdinalIgnoreCase).ToList();
        return new ProviderTenantAccessResponse
        {
            Provider = definition.Id,
            Tenants = rows
        };
    }

    public async Task<ProviderSettingsRecord> SetSettingsAsync(string provider, SetProviderSettingsRequest request, CancellationToken cancellationToken)
    {
        var definition = RequireProvider(provider);
        ArgumentNullException.ThrowIfNull(request);
        if (request.LowBalanceUsd is < 0m or > MaxLowBalanceUsd)
        {
            throw new ProviderAdminException($"lowBalanceUsd must be between 0 and {MaxLowBalanceUsd:0}.");
        }

        var record = new ProviderSettingsRecord(definition.Id, request.LowBalanceUsd is { } level ? decimal.Round(level, 2, MidpointRounding.AwayFromZero) : null, request.NewModelsStartStopped);
        await Require<IProviderSettingsRepository>().UpsertAsync(record, cancellationToken).ConfigureAwait(false);
        // The alert reads the level from the status store; do not make it wait for the next poll.
        status.SetLowBalance(definition.Id, record.LowBalanceUsd);
        return record;
    }

    /// <summary>
    /// Replaces the company key for every backend at the provider, and reads the balance with it
    /// at once so a mistyped key is seen on the page that was used to paste it.
    /// </summary>
    public async Task SetKeyAsync(string provider, string? apiKey, CancellationToken cancellationToken)
    {
        var definition = RequireProvider(provider);
        var key = apiKey?.Trim();
        if (string.IsNullOrEmpty(key) || key.Length < 8 || key.Length > 512 || key.Any(char.IsWhiteSpace))
        {
            throw new ProviderAdminException("apiKey must be the provider's key: 8 to 512 characters with no spaces.");
        }

        try
        {
            await credentials.StoreAsync(definition.Id, key, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            // The secret store refuses to seal anything under a weak pepper.
            throw new ProviderAdminException(ex.Message, 409);
        }

        InvalidateCatalog(definition.Id);
        await refresher.RefreshBalanceAsync(definition, cancellationToken).ConfigureAwait(false);
    }

    public async Task ClearKeyAsync(string provider, CancellationToken cancellationToken)
    {
        var definition = RequireProvider(provider);
        await credentials.ClearStoredAsync(definition.Id, cancellationToken).ConfigureAwait(false);
        InvalidateCatalog(definition.Id);
        await refresher.RefreshBalanceAsync(definition, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProviderOverviewItem> RefreshBalanceAsync(string provider, CancellationToken cancellationToken)
    {
        var definition = RequireProvider(provider);
        await refresher.RefreshBalanceAsync(definition, cancellationToken).ConfigureAwait(false);
        var list = await ListAsync(cancellationToken).ConfigureAwait(false);
        return list.Providers.First(p => p.Provider == definition.Id);
    }

    public async Task<ProviderSpendReport> GetSpendAsync(string provider, int days, CancellationToken cancellationToken)
    {
        var definition = RequireProvider(provider);
        var span = Math.Clamp(days, 1, MaxSpendDays);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = today.AddDays(-(span - 1));
        var rows = await Require<IProviderSpendRepository>().ListAsync(from, today, definition.Id, null, cancellationToken).ConfigureAwait(false);
        var tenants = (await Require<ITenantRepository>().ListAllAsync(cancellationToken).ConfigureAwait(false)).ToDictionary(t => t.Id);
        var keyIds = rows.Where(r => r.ApiKeyId is not null).Select(r => r.ApiKeyId!.Value).Distinct().ToList();
        var keys = keyIds.Count == 0 ? new Dictionary<Guid, ApiKeyRecord>() : (await Require<IApiKeyRepository>().GetByIdsAsync(keyIds, cancellationToken).ConfigureAwait(false)).ToDictionary(k => k.Id);
        var byDay = rows.GroupBy(r => r.UsageDate).ToDictionary(g => g.Key, g => (g.Sum(r => r.Cost), g.Sum(r => r.RequestCount)));
        var daily = new List<ProviderSpendDay>(span);
        for (var day = from; day <= today; day = day.AddDays(1))
        {
            // Every day of the span, so a day with nothing spent is a zero and not a gap.
            daily.Add(byDay.TryGetValue(day, out var totals) ? new ProviderSpendDay(day, totals.Item1, totals.Item2) : new ProviderSpendDay(day, 0m, 0));
        }

        return new ProviderSpendReport
        {
            Provider = definition.Id,
            From = from,
            To = today,
            TotalUsd = rows.Sum(r => r.Cost),
            Requests = rows.Sum(r => r.RequestCount),
            Daily = daily,
            ByTenant = Slice(rows.GroupBy(r => r.TenantId), g => g.Key?.ToString() ?? string.Empty, g => g.Key is { } id && tenants.TryGetValue(id, out var t) ? t.Slug : g.Key is null ? "(no key)" : "(deleted tenant)", _ => null),
            ByKey = Slice(rows.GroupBy(r => r.ApiKeyId), g => g.Key?.ToString() ?? string.Empty, g => g.Key is { } id && keys.TryGetValue(id, out var k) ? string.IsNullOrWhiteSpace(k.Label) ? k.KeyPrefix : k.Label! : g.Key is null ? "(no key)" : "(deleted key)", g => g.First().TenantId is { } tenantId && tenants.TryGetValue(tenantId, out var t) ? t.Slug : null),
            ByModel = Slice(rows.GroupBy(r => r.ModelId, StringComparer.OrdinalIgnoreCase), g => g.Key, g => g.Key, _ => null),
        };
    }

    /// <summary>
    /// The provider's balance readings set beside the gateway's charges: this month (UTC) by
    /// default, or the last <paramref name = "days"/> days.
    /// </summary>
    public async Task<ProviderReconciliationReport> GetReconciliationAsync(string provider, int? days, CancellationToken cancellationToken)
    {
        var definition = RequireProvider(provider);
        var history = Require<IProviderBalanceHistoryRepository>();
        var now = DateTimeOffset.UtcNow;
        var from = days is { } span ? now.AddDays(-Math.Clamp(span, 1, MaxSpendDays)) : new DateTimeOffset(now.UtcDateTime.Year, now.UtcDateTime.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var threshold = ProviderStatusRefreshService.ReadThreshold(services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>());
        return await ProviderReconciler.ReconcileAsync(history, definition.Id, from, now, threshold, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProviderCatalogResponse> GetCatalogAsync(string provider, string? query, CancellationToken cancellationToken)
    {
        var definition = RequireProvider(provider);
        var models = await LoadCatalogAsync(definition, cancellationToken).ConfigureAwait(false);
        var terms = (query ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var matched = models.Where(m => terms.All(term => m.Id.Contains(term, StringComparison.OrdinalIgnoreCase) || (m.Name?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false))).ToList();
        return new ProviderCatalogResponse
        {
            Provider = definition.Id,
            Total = matched.Count,
            Models = matched.Take(MaxCatalogResults).Select(m => new ProviderCatalogItem { Id = m.Id, Name = m.Name, ContextLength = m.ContextLength, InputPricePerMillionTokens = m.InputPricePerMillionTokens, OutputPricePerMillionTokens = m.OutputPricePerMillionTokens, CachedInputPricePerMillionTokens = m.CachedInputPricePerMillionTokens, PricePerRequest = m.PricePerRequest, Added = registry.ModelExists(m.Id), }).ToList(),
        };
    }

    /// <summary>
    /// Registers catalogue models under the provider's own ids, with the price and context length
    /// the catalogue gives. Each model is reported on its own: one that is already registered does
    /// not stop the others.
    /// </summary>
    public async Task<AddProviderModelsResponse> AddModelsAsync(string provider, IReadOnlyList<string> modelIds, AdminActor actor, CancellationToken cancellationToken)
    {
        var definition = RequireProvider(provider);
        var ids = (modelIds ?? []).Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (ids.Count == 0)
        {
            throw new ProviderAdminException("models must name at least one catalogue model.");
        }

        if (ids.Count > MaxModelsPerAdd)
        {
            throw new ProviderAdminException($"At most {MaxModelsPerAdd} models can be added at once.");
        }

        var catalog = (await LoadCatalogAsync(definition, cancellationToken).ConfigureAwait(false)).ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
        var settings = (await ListSettingsAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(s => string.Equals(s.Provider, definition.Id, StringComparison.OrdinalIgnoreCase));
        var startStopped = settings?.NewModelsStartStopped ?? false;
        var results = new List<AddProviderModelResult>(ids.Count);
        foreach (var id in ids)
        {
            if (!catalog.TryGetValue(id, out var item))
            {
                results.Add(new AddProviderModelResult(id, false, $"{definition.DisplayName} lists no model '{id}'."));
                continue;
            }

            var request = new AdminModelWriteRequest
            {
                Model = new ModelConfig
                {
                    Id = item.Id,
                    Url = definition.UpstreamBaseUrl,
                    // The provider's variable, never a key of the model's own: the company key is
                    // then replaced in one place for every model.
                    UpstreamAuth = new UpstreamAuthConfig
                    {
                        Type = "bearer",
                        EnvVar = definition.DefaultEnvVar
                    },
                    MaxContextLength = item.ContextLength ?? 0,
                    Provider = definition.Id,
                    State = startStopped ? ModelRouteStates.Stopped : ModelRouteStates.Serving,
                },
                Pricing = item.InputPricePerMillionTokens is null && item.OutputPricePerMillionTokens is null && item.PricePerRequest is null ? null : new ModelPricing
                {
                    InputPricePerMillionTokens = item.InputPricePerMillionTokens ?? 0m,
                    OutputPricePerMillionTokens = item.OutputPricePerMillionTokens ?? 0m,
                    CachedInputPricePerMillionTokens = item.CachedInputPricePerMillionTokens,
                    PricePerRequest = item.PricePerRequest,
                },
            };
            var result = await provisioning.AddAsync(request, actor, cancellationToken).ConfigureAwait(false);
            results.Add(new AddProviderModelResult(item.Id, result.Success, result.Message));
        }

        return new AddProviderModelsResponse
        {
            Results = results
        };
    }

    private async Task<IReadOnlyList<ProviderModelListItem>> LoadCatalogAsync(ModelProviderDefinition definition, CancellationToken cancellationToken)
    {
        if (definition.CatalogUrl is null)
        {
            throw new ProviderAdminException($"{definition.DisplayName} publishes no model list the gateway can read; add its models by id on the Routing page.");
        }

        var cacheKey = CatalogCacheKey(definition.Id);
        if (cache.TryGetValue(cacheKey, out IReadOnlyList<ProviderModelListItem>? cached) && cached is not null)
        {
            return cached;
        }

        var key = credentials.Resolve(definition.Id);
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ProviderAdminException($"No {definition.DisplayName} key is configured. Set {definition.DefaultEnvVar}, or store a key under Settings.", 409);
        }

        try
        {
            var models = await catalogClient.ListModelsAsync(new Uri(definition.CatalogUrl), key, cancellationToken).ConfigureAwait(false);
            cache.Set(cacheKey, models, CatalogTtl);
            return models;
        }
        catch (ProviderModelsUpstreamException ex)
        {
            throw new ProviderAdminException($"{definition.DisplayName} answered HTTP {(int)ex.StatusCode} to the model list request.", 502);
        }
        catch (HttpRequestException)
        {
            throw new ProviderAdminException($"{definition.DisplayName} could not be reached.", 502);
        }
    }

    private void InvalidateCatalog(string provider) => cache.Remove(CatalogCacheKey(provider));
    private static string CatalogCacheKey(string provider) => "provider-catalog:" + provider;
    private async Task<IReadOnlyList<ProviderSettingsRecord>> ListSettingsAsync(CancellationToken cancellationToken) => services.GetService<IProviderSettingsRepository>()is { } repository ? await repository.ListAsync(cancellationToken).ConfigureAwait(false) : [];
    private T Require<T>()
        where T : class => services.GetService<T>() ?? throw new ProviderAdminException("Provider administration requires ConnectionStrings:GatewayDb.", 503);
    /// <summary>A model counts for a provider when any of its backends is billed by it, standby included.</summary>
    private static bool UsesProvider(ModelConfig model, string provider) => string.Equals(ModelProviders.Resolve(model), provider, StringComparison.OrdinalIgnoreCase) || model.GetBackends().Any(b => string.Equals(ModelProviders.ResolveBackend(model, b), provider, StringComparison.OrdinalIgnoreCase));
    private static List<ProviderSpendSlice> Slice<TKey>(IEnumerable<IGrouping<TKey, ProviderSpendRecord>> groups, Func<IGrouping<TKey, ProviderSpendRecord>, string> id, Func<IGrouping<TKey, ProviderSpendRecord>, string> label, Func<IGrouping<TKey, ProviderSpendRecord>, string?> detail) => groups.Select(g => new ProviderSpendSlice(id(g), label(g), detail(g), g.Sum(r => r.Cost), g.Sum(r => r.RequestCount))).OrderByDescending(s => s.CostUsd).ThenByDescending(s => s.Requests).ToList();
    private static (DateOnly MonthStart, DateOnly Today) CurrentMonth()
    {
        var now = DateTime.UtcNow;
        return (new DateOnly(now.Year, now.Month, 1), DateOnly.FromDateTime(now));
    }

    private static ProviderBalanceDto? ToDto(ProviderBalanceReading? reading) => reading is null ? null : new ProviderBalanceDto
    {
        Remaining = reading.Remaining,
        Limit = reading.Limit,
        Used = reading.Used,
        Currency = reading.Currency,
        ReadAtUtc = reading.ReadAt,
        Error = reading.Error,
    };
}
