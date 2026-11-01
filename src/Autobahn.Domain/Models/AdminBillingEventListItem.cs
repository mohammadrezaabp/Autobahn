using System.Text.Json.Serialization;
using Autobahn.Domain.Identity;

namespace Autobahn.Domain.Models;
public sealed class AdminBillingEventListItem
{
    public required Guid Id { get; init; }
    public required string RequestId { get; init; }
    public Guid? ApiKeyId { get; init; }
    public string? KeyPrefix { get; init; }
    public string? Assignee { get; init; }
    public required string ModelId { get; init; }
    public string? CostCenter { get; init; }
    public long PromptTokens { get; init; }
    public long CompletionTokens { get; init; }
    public decimal? TotalCost { get; init; }
    public double DurationMs { get; init; }
    public required DateTimeOffset RecordedAt { get; init; }
}
