using Autobahn.Domain.RateLimiting;

namespace Autobahn.Application.Abstractions;
/// <param name = "TenantId">The tenant, or the anonymous partition key for unauthenticated traffic.</param>
/// <param name = "ApiKeyId">The credential, or null when anonymous.</param>
/// <param name = "ModelId">The model, or null when the request was refused before it was known.</param>
/// <param name = "Admitted">Whether the request was let through.</param>
/// <param name = "Scope">On a rejection, the scope that refused; on an admission, the tightest scope.</param>
/// <param name = "Control">Whether the decision came from the rate bucket or a concurrency cap.</param>
/// <param name = "ConfiguredRpm">The tier's configured sustained rate, for the "usage against limit" column.</param>
/// <param name = "EffectiveRpm">What was actually enforced, after adaptation.</param>
public readonly record struct RateLimitUsageEvent(string? TenantId, string? ApiKeyId, string? ModelId, bool Admitted, RateLimitScope? Scope, RateLimitControl Control, int ConfiguredRpm, int EffectiveRpm);
