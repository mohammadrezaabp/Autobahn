namespace Autobahn.Domain.Billing;
/// <summary>
/// A request to price a model's stored usage again at its current rate card: every request the
/// ledger holds for the model on the UTC days <paramref name = "From"/> to <paramref name = "To"/>,
/// both included.
/// </summary>
/// <param name = "DryRun">Report what would change and write nothing.</param>
public sealed record BillingRepriceRequest(string ModelId, DateOnly From, DateOnly To, bool DryRun);
