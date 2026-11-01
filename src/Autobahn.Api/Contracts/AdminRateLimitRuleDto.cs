using Autobahn.Domain.Configuration;
using Autobahn.Domain.RateLimiting;

namespace Autobahn.Api.Contracts;
/// <param name = "Scope">
/// <c>global</c>, <c>tenant</c>, <c>api_key</c>, <c>model</c>, <c>tenant_model</c>,
/// <c>api_key_model</c>, <c>auth_failure</c> or <c>anonymous</c>.
/// </param>
/// <param name = "Target">
/// What the rule applies to: an id for the single-subject scopes, <c>subject|model</c> for the
/// combined ones, and <c>*</c> for the scopes with one partition.
/// </param>
/// <param name = "Rpm">Sustained requests per minute; zero limits only concurrency.</param>
/// <param name = "Burst">Extra requests an idle partition may spend at once.</param>
/// <param name = "MaxConcurrentStreams">Concurrent streaming responses; zero means unlimited.</param>
public sealed record AdminRateLimitRuleDto(string Scope, string Target, int Rpm, int Burst, int MaxConcurrentStreams)
{
    /// <summary>
    /// The rule's schedule windows. Null on a PUT means "keep the windows stored for this rule";
    /// an empty array removes them. Always present on a GET.
    /// </summary>
    public List<AdminRateLimitWindowDto>? Schedule { get; init; }
    /// <summary>
    /// Whether the rule is enforced. A disabled rule keeps its tier and windows and is still returned
    /// by the GET; it simply enforces nothing.
    /// </summary>
    /// <remarks>
    /// Defaults to true, so a client that predates the field keeps writing enforced rules rather than
    /// silently switching off everything it round-trips.
    /// </remarks>
    public bool Enabled { get; init; } = true;
    /// <summary>
    /// Tokens per minute, prompt and completion together; zero means the rule does not limit tokens.
    /// </summary>
    /// <remarks>
    /// Defaults to zero, so a client that predates the field writes rules with no token limit. A
    /// client that round-trips a GET it does not fully understand must carry this field back, or
    /// the PUT removes the limit.
    /// </remarks>
    public int Tpm { get; init; }

    /// <remarks>
    /// The scope is canonicalised rather than merely trimmed. It is persisted verbatim and compared
    /// against the canonical constants everywhere else, so a spelling like <c>"Anonymous"</c> used to
    /// be stored as written and then never match the singleton it names.
    /// </remarks>
    public RateLimitRuleDefinition ToDefinition() => new(RateLimitScopeNames.Canonical(Scope), Target ?? string.Empty, Rpm, Burst, MaxConcurrentStreams)
    {
        Schedule = Schedule?.Select(static w => w.ToDefinition()).ToArray(),
        Enabled = Enabled,
        Tpm = Tpm,
    };
    public static AdminRateLimitRuleDto FromDefinition(RateLimitRuleDefinition rule) => new(rule.Scope, rule.TargetKey, rule.Rpm, rule.Burst, rule.MaxConcurrentStreams)
    {
        Schedule = rule.Windows.Select(AdminRateLimitWindowDto.FromDefinition).ToList(),
        Enabled = rule.Enabled,
        Tpm = rule.Tpm,
    };
}
