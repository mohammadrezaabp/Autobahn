using Autobahn.Application.Errors;

namespace Autobahn.Application.Abstractions;

public interface IErrorResponseWriter
{
    WrittenErrorResponse Write(
        GatewayErrorCode code,
        string? message = null,
        string? param = null,
        IReadOnlyDictionary<string, object>? details = null);
}
