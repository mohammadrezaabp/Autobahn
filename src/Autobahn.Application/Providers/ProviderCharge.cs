namespace Autobahn.Application.Providers;
/// <summary>What the gateway charged for one request at a paid provider, at the time it was recorded.</summary>
public sealed record ProviderCharge(DateTimeOffset RecordedAt, decimal Cost);
