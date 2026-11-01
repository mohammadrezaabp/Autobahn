using System.Text.Json.Serialization;

namespace Autobahn.Application.Errors;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GatewayErrorCode
{
    InvalidJson,
    RequestIncomplete,
    MissingModel,
    ModelNotAllowed,
    RequestTooLarge,
    InvalidApiKey,
    ExpiredApiKey,
    InsufficientScope,
    ProviderClosedForTenant,
    ProviderClosedForKey,
    ModelNotOnKeyList,
    SpendCapReached,
    ModelNotFound,
    RateLimitExceeded,
    QuotaExceeded,
    ConcurrencyLimitExceeded,
    BackendUnhealthy,
    UpstreamError,
    CircuitOpen,
    GatewayDraining,
    NotReady,
}
