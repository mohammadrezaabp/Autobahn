using System.Diagnostics.Metrics;
using Autobahn.Domain.Billing;
using Autobahn.Domain.Models.Overview;

namespace Autobahn.Observability.Metrics;
public sealed record KeyCostSample(string Tenant, string KeyId, string Label, string Assignee, double Cost);
