namespace Autobahn.Application.Providers;
/// <summary>One successful read of a paid provider's remaining credit, as kept for reconciliation.</summary>
public sealed record ProviderBalanceSnapshot(string Provider, DateTimeOffset ReadAt, decimal Remaining, string Currency);
