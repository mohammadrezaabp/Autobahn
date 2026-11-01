using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Autobahn.Application.Abstractions;
using Autobahn.Domain.Models;
using Autobahn.Application.Providers;

namespace Autobahn.Api.Services;

/// <summary>
/// Every few minutes: reads the credit of each paid provider a key is configured for, and works
/// out how much of its monthly cap each capped tenant has used. Both land in
/// <see cref="ProviderStatusStore"/>, which the Providers page and the alert metrics read.
/// Each reading is also kept, so the fall of the balance can be set beside what the gateway
/// charged over the same time (<see cref="ProviderReconciler"/>).
/// </summary>
public sealed class ProviderStatusRefreshService(
    IServiceScopeFactory scopeFactory,
    IProviderCredentialSource credentials,
    ProviderStatusStore status,
    IConfiguration configuration,
    ILogger<ProviderStatusRefreshService> logger) : BackgroundService
{
    public const string PollSecondsKey = "Gateway:Providers:BalancePollSeconds";

    private const int DefaultPollSeconds = 300;

    private const int MinPollSeconds = 30;

    public const string DriftPercentKey = "Gateway:Providers:DriftThresholdPercent";

    public const string DriftMinimumUsdKey = "Gateway:Providers:DriftMinimumUsd";

    /// <summary>How long balance readings are kept: a year and a little, so last year's month can still be reconciled.</summary>
    public static readonly TimeSpan ReadingRetention = TimeSpan.FromDays(400);

    /// <summary>The drift threshold from configuration, falling back to <see cref="ProviderDriftThreshold.Default"/>.</summary>
    public static ProviderDriftThreshold ReadThreshold(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        var percent = decimal.TryParse(configuration[DriftPercentKey], System.Globalization.NumberStyles.Float, culture, out var p) && p >= 0m
            ? p
            : ProviderDriftThreshold.Default.Percent;
        var minimum = decimal.TryParse(configuration[DriftMinimumUsdKey], System.Globalization.NumberStyles.Float, culture, out var m) && m >= 0m
            ? m
            : ProviderDriftThreshold.Default.MinimumUsd;
        return new ProviderDriftThreshold(percent, minimum);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var seconds = int.TryParse(configuration[PollSecondsKey], out var configured) ? configured : DefaultPollSeconds;
        if (seconds <= 0)
        {
            // 0 turns the reader off, for a host with no route to any provider.
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(MinPollSeconds, seconds));
        try
        {
            // Let the host finish starting; the first reading is not worth delaying readiness for.
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
            using var timer = new PeriodicTimer(interval);
            do
            {
                await RefreshAsync(stoppingToken).ConfigureAwait(false);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    /// <summary>One pass over every provider. Never throws: a provider that cannot be read is recorded as such.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        foreach (var provider in ModelProviders.Paid)
        {
            await RefreshBalanceAsync(provider, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await RefreshFromDatabaseAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Provider settings and cap usage could not be refreshed.");
        }
    }

    public async Task RefreshBalanceAsync(ModelProviderDefinition provider, CancellationToken cancellationToken)
    {
        var key = provider.BalanceEnvVar is { } balanceEnvVar
            ? BalanceKey(balanceEnvVar)
            : credentials.Resolve(provider.Id);
        var watched = provider.Balance != ProviderBalanceKind.None && !string.IsNullOrWhiteSpace(key);
        status.SetWatched(provider.Id, watched);
        if (!watched)
        {
            return;
        }

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var reader = scope.ServiceProvider.GetRequiredService<ProviderBalanceReader>();
            var reading = await reader.ReadAsync(provider, key!, cancellationToken).ConfigureAwait(false);
            status.SetBalance(provider.Id, reading);
            await KeepReadingAsync(scope.ServiceProvider, provider, reading, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The message, never the exception object: its request carries the key.
            logger.LogWarning("The {Provider} balance could not be read: {Reason}", provider.DisplayName, ex.Message);
            status.SetBalanceError(provider.Id, ex.Message);
        }
    }

    /// <summary>A balance key of its own, read from the environment the way company keys are.</summary>
    private string? BalanceKey(string envVar)
    {
        var value = configuration[envVar] ?? Environment.GetEnvironmentVariable(envVar);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>Stores a successful reading for reconciliation. A failure to store it is logged; the reading itself stands.</summary>
    private async Task KeepReadingAsync(
        IServiceProvider services,
        ModelProviderDefinition provider,
        ProviderBalanceReading reading,
        CancellationToken cancellationToken)
    {
        if (reading.Remaining is not { } remaining || services.GetService<IProviderBalanceHistoryRepository>() is not { } history)
        {
            return;
        }

        try
        {
            await history
                .AddAsync(
                    new ProviderBalanceSnapshot(provider.Id, reading.ReadAt ?? DateTimeOffset.UtcNow, remaining, reading.Currency),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The {Provider} balance reading could not be stored for reconciliation.", provider.DisplayName);
        }
    }

    private async Task RefreshFromDatabaseAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var settings = scope.ServiceProvider.GetService<IProviderSettingsRepository>();
        var access = scope.ServiceProvider.GetService<IProviderAccessRepository>();
        var spend = scope.ServiceProvider.GetService<IProviderSpendRepository>();
        var tenants = scope.ServiceProvider.GetService<ITenantRepository>();
        if (settings is null || access is null || spend is null || tenants is null)
        {
            return;
        }

        var stored = await settings.ListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var provider in ModelProviders.Paid)
        {
            status.SetLowBalance(
                provider.Id,
                stored.FirstOrDefault(s => string.Equals(s.Provider, provider.Id, StringComparison.OrdinalIgnoreCase))
                    ?.LowBalanceUsd);
        }

        var now = DateTimeOffset.UtcNow;
        var monthStart = new DateOnly(now.UtcDateTime.Year, now.UtcDateTime.Month, 1);
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var slugs = (await tenants.ListAllAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(t => t.Id, t => t.Slug);

        var usage = new List<ProviderTenantCapUsage>();
        foreach (var provider in ModelProviders.Paid)
        {
            var capped = (await access.ListByProviderAsync(provider.Id, cancellationToken).ConfigureAwait(false))
                .Where(a => a.ApiKeyId is null && a.MonthlyCapUsd is > 0m)
                .ToList();
            if (capped.Count == 0)
            {
                continue;
            }

            var spent = (await spend.ListAsync(monthStart, today, provider.Id, null, cancellationToken).ConfigureAwait(false))
                .Where(s => s.TenantId is not null)
                .GroupBy(s => s.TenantId!.Value)
                .ToDictionary(g => g.Key, g => g.Sum(s => s.Cost));

            foreach (var row in capped)
            {
                if (!slugs.TryGetValue(row.TenantId, out var slug))
                {
                    continue;
                }

                var used = spent.TryGetValue(row.TenantId, out var cost) ? cost : 0m;
                usage.Add(new ProviderTenantCapUsage(provider.Id, slug, (double)(used / row.MonthlyCapUsd!.Value)));
            }
        }

        status.SetCapUsage(usage);

        if (scope.ServiceProvider.GetService<IProviderBalanceHistoryRepository>() is { } history)
        {
            await history.PruneAsync(now - ReadingRetention, cancellationToken).ConfigureAwait(false);
            var threshold = ReadThreshold(configuration);
            var monthStartUtc = new DateTimeOffset(monthStart.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            foreach (var provider in status.Watched)
            {
                status.SetReconciliation(
                    provider,
                    await ProviderReconciler
                        .ReconcileAsync(history, provider, monthStartUtc, now, threshold, cancellationToken)
                        .ConfigureAwait(false));
            }
        }
    }
}
