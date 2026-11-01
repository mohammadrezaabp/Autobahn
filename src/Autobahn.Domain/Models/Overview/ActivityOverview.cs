namespace Autobahn.Domain.Models.Overview;
/// <summary>Recent admin actions read back from the audit trail.</summary>
public sealed record ActivityOverview
{
    public DateTimeOffset BuiltAtUtc { get; init; }
    public IReadOnlyList<ActivityEntry> Entries { get; init; } = [];
    /// <summary>Audit lines that could not be parsed while reading; non-zero means the file was tampered with or truncated.</summary>
    public int ParseErrors { get; init; }
    public bool Available { get; init; }
}
