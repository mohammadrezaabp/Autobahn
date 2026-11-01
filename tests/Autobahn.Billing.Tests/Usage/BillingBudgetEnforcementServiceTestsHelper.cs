using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Autobahn.Billing.Aggregates;
using Autobahn.Billing.RateCards;
using Autobahn.Billing.Usage;
using Autobahn.Application.Abstractions;
using Autobahn.Domain.Billing;
using Autobahn.Domain.Configuration;
using Autobahn.Domain.Models;

namespace Autobahn.Billing.Tests.Usage;
internal static class BillingBudgetEnforcementServiceTestsHelper
{
    internal static BillingBudgetEnforcementService CreateService(IBudgetRepository budgets, IDailyUsageRollupRepository rollups, BudgetReservationLedger? ledger = null, IRateCardRepository? rateCards = null, BudgetSpendCache? spendCache = null, PeakPricingPolicy? peakPricing = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(budgets);
        services.AddSingleton(rollups);
        if (rateCards is not null)
        {
            services.AddSingleton(rateCards);
        }

        var provider = services.BuildServiceProvider();
        return new BillingBudgetEnforcementService(provider.GetRequiredService<IServiceScopeFactory>(), ledger ?? new BudgetReservationLedger(TimeSpan.FromMinutes(2)), spendCache ?? new BudgetSpendCache(new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions())), Microsoft.Extensions.Options.Options.Create(new Autobahn.Domain.Configuration.BillingOptions()), peakPricing);
    }
}
