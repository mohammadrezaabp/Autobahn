using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Autobahn.Application.Abstractions;

namespace Autobahn.Persistence.Maintenance;
/// <summary>Fallback when no SQLite database is configured (in-memory tests, DB-less deployments).</summary>
public sealed class NullSqliteBackupService : ISqliteBackupService
{
    public Task<SqliteBackupResult> CreateBackupAsync(CancellationToken cancellationToken = default) => Task.FromResult(SqliteBackupResult.NotConfigured());
}
