namespace Autobahn.Application.Providers;
/// <summary>A UTC day's share: each stretch between readings counts on the day it ends.</summary>
public sealed record ProviderReconciliationDay(DateOnly Date, decimal ProviderUsd, decimal GatewayUsd);
