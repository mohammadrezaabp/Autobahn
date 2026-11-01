using System.Text.Json.Serialization;

namespace Autobahn.Application.Errors;
public sealed class ErrorResult
{
    [JsonPropertyName("error")]
    public required ErrorBody Error { get; init; }

    public static ErrorResult FromCode(GatewayErrorCode code, string message, string type, string? param = null, IReadOnlyDictionary<string, object>? details = null) => new()
    {
        Error = new ErrorBody
        {
            Message = message,
            Type = type,
            Code = code.ToCodeString(),
            Param = param,
            Details = details is null or { Count: 0 } ? null : new Dictionary<string, object>(details),
        },
    };
}
