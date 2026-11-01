namespace Autobahn.Domain.Models;
/// <param name = "Id">The tenant id, key id or model id the slice is for.</param>
/// <param name = "Detail">For a key: its tenant's slug.</param>
public sealed record ProviderSpendSlice(string Id, string Label, string? Detail, decimal CostUsd, int Requests);
