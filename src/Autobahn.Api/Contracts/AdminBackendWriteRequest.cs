using Autobahn.Domain.Models;

namespace Autobahn.Api.Contracts;

/// <summary>The body of a write to one backend of a model.</summary>
public sealed class AdminBackendWriteRequest
{
    public ModelBackendConfig Backend { get; set; } = new();

    /// <summary>Write-only upstream API key for this backend. Never returned on GET.</summary>
    public string? ApiKey { get; set; }

    /// <summary>When true, removes this backend's stored upstream credential and clears its upstreamAuth.</summary>
    public bool ClearApiKey { get; set; }
}
