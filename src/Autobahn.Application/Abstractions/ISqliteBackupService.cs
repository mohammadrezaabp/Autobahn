namespace Autobahn.Application.Abstractions;
/// <summary>
/// Produces a consistent, online (no-downtime) copy of the embedded SQLite database using
/// <c>VACUUM INTO</c>, which snapshots a transactionally-consistent image even while the gateway is
/// writing under WAL. Returns a degraded result (not an exception) when no relational database is
/// configured, so the admin surface can report cleanly.
/// </summary>
public interface ISqliteBackupService
{
    Task<SqliteBackupResult> CreateBackupAsync(CancellationToken cancellationToken = default);
}
