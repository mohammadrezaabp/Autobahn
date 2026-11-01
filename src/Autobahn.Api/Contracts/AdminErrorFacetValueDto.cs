using Autobahn.Domain.Models;

namespace Autobahn.Api.Contracts;
public sealed class AdminErrorFacetValueDto
{
    public required string Value { get; init; }
    public long Count { get; init; }

    public static AdminErrorFacetValueDto From(GatewayErrorFacetValue value) => new()
    {
        Value = value.Value,
        Count = value.Count,
    };
}
