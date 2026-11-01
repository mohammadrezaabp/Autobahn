namespace Autobahn.Domain.Models.Overview;
public sealed record SecretsVerificationStatus
{
    public bool HasRun { get; init; }
    public int Total { get; init; }
    public int Undecryptable { get; init; }
    public IReadOnlyList<string> UndecryptableModels { get; init; } = [];
    public DateTimeOffset? CheckedAtUtc { get; init; }
}
