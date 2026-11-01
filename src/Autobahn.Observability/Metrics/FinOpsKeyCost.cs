using System.Diagnostics.Metrics;
using Autobahn.Domain.Billing;
using Autobahn.Domain.Models.Overview;

namespace Autobahn.Observability.Metrics;
/// <summary>One API key's month-to-date cost, already resolved to a tenant slug. No key prefix.</summary>
public sealed record FinOpsKeyCost(string TenantSlug, Guid KeyId, string? Label, string? Assignee, decimal Cost);
