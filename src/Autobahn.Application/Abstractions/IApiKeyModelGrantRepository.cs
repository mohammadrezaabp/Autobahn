using Autobahn.Domain.Identity;

namespace Autobahn.Application.Abstractions;

public interface IApiKeyModelGrantRepository
{
    Task<IReadOnlyList<ApiKeyModelGrantRecord>> ListByApiKeyAsync(
        Guid apiKeyId,
        CancellationToken cancellationToken = default);

    Task ReplaceForApiKeyAsync(
        Guid apiKeyId,
        IReadOnlyList<string> modelPatterns,
        CancellationToken cancellationToken = default);

    /// <summary>Replaces the key's list with allow and deny entries.</summary>
    Task ReplaceForApiKeyAsync(
        Guid apiKeyId,
        IReadOnlyList<(string ModelPattern, GrantEffect Effect)> grants,
        CancellationToken cancellationToken = default);
}
