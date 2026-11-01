using System.Text.Json.Serialization;
using Autobahn.Domain.Identity;

namespace Autobahn.Domain.Models;
/// <summary>Body of the 409 returned when a lifecycle transition conflicts with the key's state.</summary>
public sealed class ApiKeyLifecycleConflictResponse
{
    public required string Code { get; init; }
    public required string Message { get; init; }
    /// <summary>Set for <c>key_has_usage</c>, so the console can say how much usage is at stake.</summary>
    public int? BillingEventCount { get; init; }
    /// <summary>Set for <c>key_has_usage</c>.</summary>
    public DateTimeOffset? LastUsedAt { get; init; }
}
