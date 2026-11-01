using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Autobahn.Billing.Forecast;
using Autobahn.Billing.RateCards;
using Autobahn.Billing.Reconciliation;
using Autobahn.Billing.Usage;
using Autobahn.Billing.Webhooks;
using Autobahn.Application.Abstractions;
using Autobahn.Persistence.DependencyInjection;

namespace Autobahn.Billing.DependencyInjection;

public static class BillingPersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddGatewayBillingPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(PersistenceServiceCollectionExtensions.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return services;
        }

        services.Replace(ServiceDescriptor.Scoped<IRateCardAdminService, RateCardAdminService>());

        services.AddScoped<BillingUsagePersistenceHandler>();
        services.AddScoped<BillingUsageService>();
        services.AddScoped<IBillingRepriceService, BillingRepriceService>();
        services.AddScoped<IBillingForecastService, BillingForecastService>();
        services.Replace(ServiceDescriptor.Singleton<IBudgetEnforcementService, BillingBudgetEnforcementService>());
        services.Replace(ServiceDescriptor.Singleton<IProviderSpendCapService>(sp => new ProviderSpendCapService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<ProviderSpendLedger>(),
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Autobahn.Domain.Configuration.BillingOptions>>(),
            peakPricing: sp.GetService<Autobahn.Domain.Billing.PeakPricingPolicy>())));

        // All three registrations must resolve the SAME instance. Registering the interface by
        // implementation type (Singleton<IUsagePersistenceHandler, BillingUsageBatchPersistenceHandler>)
        // builds a second, independent singleton: the hosted-service copy runs the flush loop with an
        // empty buffer while the copy that actually receives usage events is never started, so batches
        // below UsageWriterBatchSize are never flushed and pending events are lost at shutdown.
        services.AddSingleton<BillingUsageBatchPersistenceHandler>();
        services.AddHostedService(sp => sp.GetRequiredService<BillingUsageBatchPersistenceHandler>());
        services.Replace(ServiceDescriptor.Singleton<IUsagePersistenceHandler>(
            sp => sp.GetRequiredService<BillingUsageBatchPersistenceHandler>()));
        services.Replace(ServiceDescriptor.Scoped<IBillingUsageService, BillingUsageService>());
        services.AddScoped<DailyUsageWebhookPublisher>();
        services.AddHostedService<DailyUsageWebhookHostedService>();

        // Reconciliation only exists where both sides do — with no persistence there is no ledger and
        // no rollups to compare.
        services.AddScoped<IBillingReconciliationService, BillingReconciliationService>();
        services.AddHostedService<BillingReconciliationHostedService>();
        return services;
    }
}
