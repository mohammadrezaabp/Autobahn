namespace Autobahn.Application.Abstractions;
/// <summary>Outcome of a hot backup attempt.</summary>
public sealed record SqliteBackupResult(bool Succeeded, string? Path, long SizeBytes, string IntegrityCheck, string? Error)
{
    public static SqliteBackupResult NotConfigured() => new(false, null, 0, "skipped", "No relational database is configured; nothing to back up.");
}
