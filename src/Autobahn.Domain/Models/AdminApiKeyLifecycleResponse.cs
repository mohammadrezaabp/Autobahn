using System.Text.Json.Serialization;
using Autobahn.Domain.Identity;

namespace Autobahn.Domain.Models;
public sealed class AdminApiKeyLifecycleResponse
{
    public required Guid Id { get; init; }
    public required string KeyPrefix { get; init; }
    public string? Label { get; init; }
    /// <summary>One of <c>active</c>, <c>expired</c>, <c>revoked</c>, <c>archived</c>, <c>deleted</c>.</summary>
    public required string Status { get; init; }
    /// <summary>False once the key row itself has been permanently deleted; its history remains.</summary>
    public required bool Exists { get; init; }
    public required IReadOnlyList<AdminApiKeyLifecycleEntry> Events { get; init; }
}
