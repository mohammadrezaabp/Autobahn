namespace Autobahn.Domain.RateLimiting;
/// <summary>
/// The full set of rules that applies to one request, split into the two stages the gateway
/// evaluates them in.
/// </summary>
/// <remarks>
/// Immutable and shared: the resolver caches one instance per distinct
/// (tenant, key, model, config version) combination, so the hot path is a dictionary lookup rather
/// than a rule-set rebuild. Nothing here holds mutable state — the counters live in the store, keyed
/// by the partition keys these rules name.
/// </remarks>
public sealed class RateLimitPlan
{
    public static RateLimitPlan Empty { get; } = new([], 0);

    public RateLimitPlan(RateLimitRule[] rules, int modelStageIndex)
    {
        Rules = rules;
        ModelStageIndex = modelStageIndex;
    }

    /// <summary>Every rule, model-independent ones first.</summary>
    public RateLimitRule[] Rules { get; }
    /// <summary>Index of the first model-scoped rule; equal to <c>Rules.Length</c> when there are none.</summary>
    public int ModelStageIndex { get; }
    /// <summary>
    /// The rules that can be evaluated before the request body is parsed: global, tenant, and key.
    /// These gate the parse, so a caller already over budget is refused without the gateway paying
    /// to read what it sent.
    /// </summary>
    public ReadOnlySpan<RateLimitRule> IdentityRules => Rules.AsSpan(0, ModelStageIndex);
    /// <summary>The rules that need the model: per-model, tenant×model, and key×model.</summary>
    public ReadOnlySpan<RateLimitRule> ModelRules => Rules.AsSpan(ModelStageIndex);
    public bool IsEmpty => Rules.Length == 0;

    /// <summary>Whether any rule in the set limits tokens, so a caller can skip the reservation entirely.</summary>
    public bool HasTokenRules
    {
        get
        {
            foreach (var rule in Rules)
            {
                if (rule.Policy.EnforcesTokens)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Whether any rule in the set caps concurrency, so the router can skip the call entirely.</summary>
    public bool HasConcurrencyRules
    {
        get
        {
            foreach (var rule in Rules)
            {
                if (rule.Policy.EnforcesConcurrency)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
