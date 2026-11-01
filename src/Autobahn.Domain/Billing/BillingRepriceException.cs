namespace Autobahn.Domain.Billing;
/// <summary>Thrown for a reprice that cannot run as asked; carries the status to answer with.</summary>
public sealed class BillingRepriceException(string message, int statusCode = 400) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
