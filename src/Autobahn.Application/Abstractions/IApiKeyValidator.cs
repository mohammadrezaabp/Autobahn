using Autobahn.Application.Security;

namespace Autobahn.Application.Abstractions;

public interface IApiKeyValidator
{
    Task<ApiKeyValidationResult> ValidateAsync(string? apiKey, CancellationToken cancellationToken = default);

    void InvalidateCache(Guid apiKeyId);
}
