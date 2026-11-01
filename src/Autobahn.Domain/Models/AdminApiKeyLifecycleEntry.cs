using System.Text.Json.Serialization;
using Autobahn.Domain.Identity;

namespace Autobahn.Domain.Models;
public sealed class AdminApiKeyLifecycleEntry
{
    public required Guid Id { get; init; }
    public required string Event { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public string? KeyPrefix { get; init; }
    public string? Label { get; init; }
    public Guid? ActorApiKeyId { get; init; }
    public bool HadUsage { get; init; }
}
