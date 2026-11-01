namespace Autobahn.Domain.Models;
/// <summary>
/// How the token counts on a <see cref = "UsageEvent"/> were obtained, which determines how they may
/// be priced.
/// </summary>
public enum UsageTokenSource
{
    /// <summary>Upstream reported input and output separately; each is priced at its own rate.</summary>
    Split = 0,
    /// <summary>
    /// Upstream reported only a combined total, carried in <see cref = "UsageEvent.TotalTokens"/>. The
    /// split is unknown, so pricing must apply a deliberate policy rather than assume the input rate.
    /// </summary>
    TotalOnly = 1,
    /// <summary>
    /// No authoritative usage arrived — typically the client disconnected before the terminal usage
    /// frame — so the counts are approximated from the number of response frames actually streamed.
    /// </summary>
    /// <remarks>
    /// Recording nothing in this situation meant a client that disconnects just before completion
    /// received free inference, while the upstream had already generated (and charged for) the
    /// tokens. Kept distinct from authoritative usage so it can be reconciled or excluded.
    /// </remarks>
    Estimated = 2,
}
