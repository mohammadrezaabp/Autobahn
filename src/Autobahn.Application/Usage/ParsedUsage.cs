namespace Autobahn.Application.Usage;
/// <summary>
/// The outcome of reading a <c>usage</c> object from an upstream response.
/// </summary>
/// <remarks>
/// Keeping <see cref = "UsageParseKind.TotalOnly"/> distinct from <see cref = "UsageParseKind.Split"/>
/// is a billing-correctness requirement. The previous parser folded a lone <c>total_tokens</c> into
/// the prompt-token field, so the whole total was priced at the input rate — typically several times
/// cheaper than output — and every model reporting only a total was silently under-billed with no
/// signal that anything had been assumed.
/// </remarks>
public readonly record struct ParsedUsage(UsageParseKind Kind, long PromptTokens, long CompletionTokens, long TotalTokens)
{
    public static readonly ParsedUsage None = new(UsageParseKind.None, 0, 0, 0);
    /// <summary>
    /// What the upstream says it charged for the request, in USD, when it says so
    /// (<c>usage.cost</c>). Preferred over a rate card: it already accounts for cached tokens, the
    /// route the provider picked and prices that are not per token.
    /// </summary>
    public decimal? Cost { get; init; }
    /// <summary>Prompt tokens the upstream served from its cache; part of <see cref = "PromptTokens"/>.</summary>
    public long CachedPromptTokens { get; init; }
    /// <summary>
    /// Audio tokens on either side (<c>*_tokens_details.audio_tokens</c>); part of the prompt and
    /// completion counts, and dearer than text at the providers that report them.
    /// </summary>
    public long AudioTokens { get; init; }

    public static ParsedUsage CostOnly(decimal cost) => new(UsageParseKind.CostOnly, 0, 0, 0)
    {
        Cost = cost
    };
    public static ParsedUsage Split(long promptTokens, long completionTokens) => new(UsageParseKind.Split, promptTokens, completionTokens, promptTokens + completionTokens);
    public static ParsedUsage TotalOnly(long totalTokens) => new(UsageParseKind.TotalOnly, 0, 0, totalTokens);
    public bool HasUsage => Kind != UsageParseKind.None;
    /// <summary>Total tokens however they were reported, for quota accounting that does not price them.</summary>
    public long BillableTokenTotal => Kind == UsageParseKind.Split ? PromptTokens + CompletionTokens : TotalTokens;
}
