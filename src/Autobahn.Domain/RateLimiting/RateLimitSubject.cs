namespace Autobahn.Domain.RateLimiting;
/// <summary>
/// Who and what a request is, in the terms the rule set is built from. A struct passed by
/// <c>in</c> so resolving costs no allocation.
/// </summary>
/// <param name = "TenantId">The authenticated tenant id, or null for anonymous traffic.</param>
/// <param name = "TenantSlug">
/// The tenant's human-readable slug, carried so a <c>tenant</c> or <c>tenant_model</c> rule can be
/// written against either spelling. The id is what the bucket is keyed on and what an operator gets
/// from the admin API; the slug is what they actually know the customer by, and a rule targeting it
/// used to be accepted, persisted, displayed — and silently never matched.
/// </param>
/// <param name = "PlanSlug">The tenant's plan, which selects a tier when no per-tenant rule exists.</param>
/// <param name = "ApiKeyId">The credential the request arrived with, or null when anonymous.</param>
/// <param name = "PartitionKey">
/// What the tenant scope counts against: the tenant id when authenticated, <c>anon:&lt;block&gt;</c>
/// otherwise. Resolved by the proxy layer, which is the only place that can see the connection.
/// </param>
public readonly record struct RateLimitSubject(string? TenantId, string? TenantSlug, string? PlanSlug, string? ApiKeyId, string PartitionKey);
