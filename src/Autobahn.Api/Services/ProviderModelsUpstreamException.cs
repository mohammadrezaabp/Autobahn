using System.Net;

namespace Autobahn.Api.Services;

public sealed class ProviderModelsUpstreamException(HttpStatusCode statusCode) : Exception(
    $"Upstream model list returned HTTP {(int)statusCode}.")
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}
