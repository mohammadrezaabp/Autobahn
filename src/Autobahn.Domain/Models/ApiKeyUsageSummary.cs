using System.Text.Json.Serialization;
using Autobahn.Domain.Identity;

namespace Autobahn.Domain.Models;
public sealed class ApiKeyUsageSummary
{
    public int RequestCount { get; init; }
    public long PromptTokens { get; init; }
    public long CompletionTokens { get; init; }
    public decimal TotalCost { get; init; }
}
