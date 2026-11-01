namespace Autobahn.Domain.Billing;
/// <summary>
/// The admin-editable price for a model, in units of currency per million tokens.
/// </summary>
public sealed class ModelPricing
{
    public decimal InputPricePerMillionTokens { get; set; }
    public decimal OutputPricePerMillionTokens { get; set; }
    /// <summary>
    /// Price of cached prompt tokens. On a write, null keeps what is stored; on a read, null means
    /// cached tokens are priced as ordinary input.
    /// </summary>
    public decimal? CachedInputPricePerMillionTokens { get; set; }
    /// <summary>Flat charge per request. On a write, null keeps what is stored.</summary>
    public decimal? PricePerRequest { get; set; }
    /// <summary>Read-only on write requests; reported on GET so the UI can label amounts.</summary>
    public string Currency { get; set; } = "USD";
}
