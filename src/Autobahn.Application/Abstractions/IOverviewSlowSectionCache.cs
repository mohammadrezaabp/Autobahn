using Autobahn.Domain.Models.Overview;

namespace Autobahn.Application.Abstractions;

/// <summary>
/// The last-built database-backed Overview sections, readable without blocking. The summary and
/// its Attention list read from here so the live stream never waits on a query; the sections are
/// rebuilt on their own cadence by the composition root.
/// </summary>
public interface IOverviewSlowSectionCache
{
    FinOpsOverview? FinOps { get; }

    PolicyOverview? Policy { get; }

    ControlPlaneOverview? ControlPlane { get; }

    TenantsOverview? Tenants { get; }

    RateLimitOverview? RateLimits => null;
}
