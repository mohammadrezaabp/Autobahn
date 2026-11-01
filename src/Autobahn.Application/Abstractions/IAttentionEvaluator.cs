using Autobahn.Domain.Models.Overview;

namespace Autobahn.Application.Abstractions;
/// <summary>Turns the Overview's sections into the ranked list of things that need an operator.</summary>
public interface IAttentionEvaluator
{
    IReadOnlyList<AttentionItem> Evaluate(AttentionInputs inputs);
}
