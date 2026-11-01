namespace Autobahn.Domain.Models;
public sealed class SkippedAdminApiKey
{
    public Guid Id { get; init; }
    /// <summary>One of the <see cref = "MoveApiKeySkipCodes"/>.</summary>
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}
