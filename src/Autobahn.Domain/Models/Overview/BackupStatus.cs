namespace Autobahn.Domain.Models.Overview;
public sealed record BackupStatus
{
    public DateTimeOffset AttemptedAtUtc { get; init; }
    public bool Succeeded { get; init; }
    public string? Path { get; init; }
    public long SizeBytes { get; init; }
    public string IntegrityCheck { get; init; } = string.Empty;
    public string? Error { get; init; }
}
