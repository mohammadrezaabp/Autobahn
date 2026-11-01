using System.Text.Json.Serialization;
using Autobahn.Domain.Identity;

namespace Autobahn.Domain.Models;
public sealed class AdminApiKeyCreatedResponse
{
    public required Guid Id { get; init; }
    public required string Secret { get; init; }
    public required string KeyPrefix { get; init; }
    public required ApiKeyRole Role { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public string? Label { get; init; }
    public string? Assignee { get; init; }
    public string? Description { get; init; }
    public string? CostCenter { get; init; }
}
