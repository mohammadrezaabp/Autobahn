using Autobahn.Domain.Models;

namespace Autobahn.Application.Abstractions;

/// <summary>
/// Exposes the in-memory monthly quota usage for snapshotting to durable storage and restoring it
/// on startup, so quota usage survives gateway container recreation.
/// </summary>
public interface IQuotaUsageSnapshotSource
{
    /// <summary>Exports the current monthly usage for every tracked partition.</summary>
    IReadOnlyList<QuotaUsageSnapshot> ExportUsage();

    /// <summary>
    /// Seeds the in-memory usage from a persisted snapshot. Entries whose period does not match the
    /// current billing month are ignored so a stale month never lingers.
    /// </summary>
    void HydrateUsage(IReadOnlyList<QuotaUsageSnapshot> usages);
}
