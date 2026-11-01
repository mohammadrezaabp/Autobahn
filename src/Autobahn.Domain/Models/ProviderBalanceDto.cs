namespace Autobahn.Domain.Models;
public sealed class ProviderBalanceDto
{
    /// <summary>Credit left, in <see cref = "Currency"/>; null when the provider reported no limit.</summary>
    public decimal? Remaining { get; init; }
    public decimal? Limit { get; init; }
    public decimal? Used { get; init; }
    public string Currency { get; init; } = "USD";
    /// <summary>When the figures were last read successfully.</summary>
    public DateTimeOffset? ReadAtUtc { get; init; }
    /// <summary>Why the last read failed; null when it succeeded.</summary>
    public string? Error { get; init; }
}
