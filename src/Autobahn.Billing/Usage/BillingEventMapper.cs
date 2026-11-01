using Autobahn.Domain.Billing;
using Autobahn.Domain.Models;

namespace Autobahn.Billing.Usage;

internal static class BillingEventMapper
{
    public static BillingEventRecord FromUsageEvent(UsageEvent usageEvent, BillingCostBreakdown? costs = null) =>
        BillingEventFactory.FromUsageEvent(usageEvent, costs);
}
