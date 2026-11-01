namespace Autobahn.Domain.Models.Overview;
/// <summary>Slower control-plane facts served by <c>/admin/api/overview/control-plane</c>.</summary>
public sealed record ControlPlaneOverview
{
    public DateTimeOffset BuiltAtUtc { get; init; }
    public SecretsVerificationStatus Secrets { get; init; } = new();
    public BackupStatus? LastBackup { get; init; }
    public int BackupCount { get; init; }
    public DatabaseStatus Database { get; init; } = new();
    public DateTimeOffset? AuditLastEntryUtc { get; init; }
    public DateTimeOffset? ConfigLastReloadUtc { get; init; }
    public int ModelCount { get; init; }
}
