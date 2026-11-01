using System.Text.Json;
using Autobahn.Application.Abstractions;

namespace Autobahn.Api.Contracts;
/// <summary>One rule a save added, changed or removed.</summary>
public sealed class AdminRateLimitRuleChangeDto
{
    /// <summary><c>added</c>, <c>changed</c> or <c>removed</c>.</summary>
    public string Kind { get; set; } = "changed";
    /// <summary>
    /// The rule's identity, <c>scope:target</c> in lower case — the same id the usage report uses.
    /// Null on entries written before ids were recorded.
    /// </summary>
    public string? RuleId { get; set; }
    /// <summary>The tier before, as <c>120rpm+20burst/4streams</c>, with <c>+Nw</c> for windows and <c>off</c> when switched off. Null when added.</summary>
    public string? Before { get; set; }
    /// <summary>The tier after, in the same form. Null when removed.</summary>
    public string? After { get; set; }
    /// <summary>The recorded sentence, only on entries that have no <see cref = "RuleId"/>.</summary>
    public string? Summary { get; set; }
}
