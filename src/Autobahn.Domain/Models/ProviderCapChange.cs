namespace Autobahn.Domain.Models;
/// <summary>The result of a cap change, with the value it replaced for the audit log.</summary>
public sealed record ProviderCapChange(string Provider, decimal? MonthlyCapUsd, decimal? PreviousMonthlyCapUsd);
