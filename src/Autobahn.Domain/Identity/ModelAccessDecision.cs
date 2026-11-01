using Autobahn.Domain.Models;

namespace Autobahn.Domain.Identity;
/// <summary>
/// Whether a key may call a model, and which rule decided it.
/// </summary>
/// <param name = "Rule">
/// The grant entry that decided: the allow entry that let the model through, or the deny entry
/// that stopped it. Null when no entry matched, and for the provider switches.
/// </param>
public readonly record struct ModelAccessDecision(ModelAccessRefusal Refusal, string Provider, string? Rule = null)
{
    public bool IsAllowed => Refusal == ModelAccessRefusal.None;

    public static ModelAccessDecision Allowed(string provider = ModelProviders.SelfHosted, string? rule = null) => new(ModelAccessRefusal.None, provider, rule);
}
