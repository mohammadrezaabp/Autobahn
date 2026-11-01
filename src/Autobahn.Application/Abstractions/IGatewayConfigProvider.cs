using Autobahn.Domain.Configuration;

namespace Autobahn.Application.Abstractions;

/// <summary>
/// Exposes the current database-backed configuration snapshot to the request hot path. The returned
/// value is an immutable snapshot swapped atomically by the syncer, so callers can read it without
/// locking and never observe a torn update. Always registered — it returns
/// <see cref="GatewayConfigSnapshot.Defaults"/> until the first successful database load (or always,
/// when no database is configured).
/// </summary>
public interface IGatewayConfigProvider
{
    GatewayConfigSnapshot Current { get; }
}
