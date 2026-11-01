using Microsoft.EntityFrameworkCore;
using Autobahn.Persistence.Tests.Infrastructure;

namespace Autobahn.Persistence.Tests.Configuration;

public sealed class EntityConfigurationTests
{
    [Fact]
    public void Model_HasUniqueTenantSlugIndex()
    {
        using var db = PersistenceTestDbContextFactory.CreateInMemory(nameof(Model_HasUniqueTenantSlugIndex));
        var model = db.Model;

        var tenant = model.FindEntityType(typeof(Autobahn.Persistence.Entities.TenantEntity))!;
        tenant.GetIndexes().Should().Contain(i => i.IsUnique && i.Properties.Any(p => p.Name == "Slug"));
    }

    [Fact]
    public void Model_MapsIdentityTables()
    {
        using var db = PersistenceTestDbContextFactory.CreateInMemory(nameof(Model_MapsIdentityTables));
        var model = db.Model;

        model.FindEntityType(typeof(Autobahn.Persistence.Entities.TenantEntity))!
            .GetTableName().Should().Be("tenants");

        model.FindEntityType(typeof(Autobahn.Persistence.Entities.ApiKeyEntity))!
            .GetTableName().Should().Be("api_keys");

        model.FindEntityType(typeof(Autobahn.Persistence.Entities.ModelGrantEntity))!
            .GetTableName().Should().Be("model_grants");
    }

    [Fact]
    public void Model_MapsBillingTables()
    {
        using var db = PersistenceTestDbContextFactory.CreateInMemory(nameof(Model_MapsBillingTables));
        var model = db.Model;

        model.FindEntityType(typeof(Autobahn.Persistence.Entities.RateCardEntity))!
            .GetTableName().Should().Be("rate_cards");

        model.FindEntityType(typeof(Autobahn.Persistence.Entities.PlanEntity))!
            .GetTableName().Should().Be("plans");

        model.FindEntityType(typeof(Autobahn.Persistence.Entities.BudgetEntity))!
            .GetTableName().Should().Be("budgets");

        model.FindEntityType(typeof(Autobahn.Persistence.Entities.BillingEventEntity))!
            .GetTableName().Should().Be("billing_events");

        model.FindEntityType(typeof(Autobahn.Persistence.Entities.DailyUsageRollupEntity))!
            .GetTableName().Should().Be("daily_usage_rollups");

        model.FindEntityType(typeof(Autobahn.Persistence.Entities.QuotaAllocationEntity))!
            .GetTableName().Should().Be("quota_allocations");

        model.FindEntityType(typeof(Autobahn.Persistence.Entities.QuotaUsageEntity))!
            .GetTableName().Should().Be("quota_usages");
    }

    [Fact]
    public void Model_HasQuotaIndexes()
    {
        using var db = PersistenceTestDbContextFactory.CreateInMemory(nameof(Model_HasQuotaIndexes));
        var model = db.Model;

        var allocations = model.FindEntityType(typeof(Autobahn.Persistence.Entities.QuotaAllocationEntity))!;
        var allocationIndexColumns = new[] { "TenantId", "PeriodStart", "PeriodEnd" };
        allocations.GetIndexes().Should().Contain(i =>
            i.IsUnique &&
            i.Properties.Select(p => p.Name).SequenceEqual(allocationIndexColumns));

        var usage = model.FindEntityType(typeof(Autobahn.Persistence.Entities.QuotaUsageEntity))!;
        var usageIndexColumns = new[] { "TenantId", "PeriodStart" };
        usage.GetIndexes().Should().Contain(i =>
            i.IsUnique &&
            i.Properties.Select(p => p.Name).SequenceEqual(usageIndexColumns));
    }

    [Fact]
    public void Model_HasUniqueBillingEventRequestIdIndex()
    {
        using var db = PersistenceTestDbContextFactory.CreateInMemory(nameof(Model_HasUniqueBillingEventRequestIdIndex));
        var model = db.Model;

        var billingEvent = model.FindEntityType(typeof(Autobahn.Persistence.Entities.BillingEventEntity))!;
        billingEvent.GetIndexes().Should().Contain(i => i.IsUnique && i.Properties.Any(p => p.Name == "RequestId"));
    }
}
