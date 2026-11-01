using System.Diagnostics.Metrics;
using Autobahn.Domain.Billing;
using Autobahn.Domain.Models.Overview;

namespace Autobahn.Observability.Metrics;
public sealed record TenantCostSample(string Tenant, double Cost);
