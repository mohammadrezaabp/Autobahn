namespace Autobahn.Domain.Models;
/// <summary>One selectable filter value and how many errors carry it.</summary>
public sealed record GatewayErrorFacetValue(string Value, long Count);
