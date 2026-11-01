using Autobahn.Domain.Billing;

namespace Autobahn.Application.Abstractions;

public interface IDailyUsageRollupAggregator
{
    IReadOnlyList<DailyUsageRollupRecord> Aggregate(IEnumerable<BillingEventRecord> events);
}
