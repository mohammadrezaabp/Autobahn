namespace Autobahn.Application.Abstractions;

public interface IApiKeyLastUsedTracker
{
    ValueTask TouchAsync(Guid apiKeyId, DateTimeOffset atUtc, CancellationToken cancellationToken = default);
}
