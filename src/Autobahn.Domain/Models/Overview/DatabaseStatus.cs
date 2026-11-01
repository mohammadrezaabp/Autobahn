namespace Autobahn.Domain.Models.Overview;
public sealed record DatabaseStatus
{
    public bool Configured { get; init; }
    public string? Path { get; init; }
    public long? SizeBytes { get; init; }
    public string? JournalMode { get; init; }
}
