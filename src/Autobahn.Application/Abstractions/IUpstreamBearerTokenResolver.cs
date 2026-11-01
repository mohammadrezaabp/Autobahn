using Autobahn.Domain.Models;

namespace Autobahn.Application.Abstractions;

public interface IUpstreamBearerTokenResolver
{
    string? ResolveBearerToken(UpstreamAuthConfig? upstreamAuth);
}
