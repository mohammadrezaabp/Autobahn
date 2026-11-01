using System.Text.Json.Serialization;
using Autobahn.Domain.Identity;

namespace Autobahn.Domain.Models;
public sealed class AdminApiKeyUsageResponse
{
    public required Guid Id { get; init; }
    public required string KeyPrefix { get; init; }
    public string? Label { get; init; }
    public string? Assignee { get; init; }
    public string? CostCenter { get; init; }
    public required DateOnly FromDate { get; init; }
    public required DateOnly ToDate { get; init; }
    public required ApiKeyUsageSummary Summary { get; init; }
    public required IReadOnlyList<AdminBillingEventListItem> Events { get; init; }
}
