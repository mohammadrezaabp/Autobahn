namespace Autobahn.Domain.Models.Overview;
/// <summary>One tenant or API key refused in the last hour.</summary>
/// <param name = "Key">
/// A tenant id or an API key id. Anonymous callers are keyed <c>anonymous:1</c>, <c>anonymous:2</c>… —
/// their client address block is not carried in this section.
/// </param>
/// <param name = "Label">Tenant slug or key label (else its public prefix); null when it could not be resolved.</param>
/// <param name = "TenantSlug">For a key, the tenant that owns it, when known.</param>
/// <param name = "Anonymous">True for an unauthenticated caller's partition.</param>
public sealed record RateLimitRefusedSubject(string Key, string? Label, string? TenantSlug, bool Anonymous, long Decisions, long Refused);
