namespace Autobahn.Domain.Billing;
public sealed class ModelPricingUpdateResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public int StatusCode { get; init; } = 200;

    public static ModelPricingUpdateResult Ok(string message) => new()
    {
        Success = true,
        Message = message,
        StatusCode = 200
    };
    public static ModelPricingUpdateResult Fail(string message, int statusCode) => new()
    {
        Success = false,
        Message = message,
        StatusCode = statusCode
    };
}
