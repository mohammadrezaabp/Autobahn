namespace Autobahn.Domain.Models;
public sealed record ProviderSpendDay(DateOnly Date, decimal CostUsd, int Requests);
