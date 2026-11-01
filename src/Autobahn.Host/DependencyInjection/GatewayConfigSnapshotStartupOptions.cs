using Autobahn.Domain.Configuration;

namespace Autobahn.Host.DependencyInjection;
/// <summary>
/// Startup budget for the database-backed configuration snapshot. Bound from the same section as
/// <see cref = "GatewayConfigSnapshotOptions"/> (<c>Gateway:ConfigSnapshot</c>).
/// </summary>
public sealed class GatewayConfigSnapshotStartupOptions
{
    public const string SectionName = GatewayConfigSnapshotOptions.SectionName;
    /// <summary>
    /// Total time the host waits for the first snapshot before failing startup. The per-attempt
    /// backoff is still capped by <see cref = "GatewayConfigSnapshotOptions.InitialLoadMaxBackoffSeconds"/>.
    /// </summary>
    public int InitialLoadTimeoutSeconds { get; set; } = 60;
}
