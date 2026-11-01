namespace Autobahn.Application.Usage;
/// <summary>
/// How much is actually known about a response's token usage.
/// </summary>
public enum UsageParseKind
{
    /// <summary>No usage could be read: absent, malformed, or unusable values.</summary>
    None = 0,
    /// <summary>Input and output tokens were reported separately and can be priced at their own rates.</summary>
    Split = 1,
    /// <summary>
    /// Only a combined total was reported. The input/output split — which is what pricing depends on
    /// — is unknown.
    /// </summary>
    TotalOnly = 2,
    /// <summary>
    /// No token counts, but the upstream reported what the request cost (a per-call price such as
    /// rerank). There is nothing to price by rate card and nothing to count against a token limit.
    /// </summary>
    CostOnly = 3,
}
