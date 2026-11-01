namespace Autobahn.Application.Security;
public sealed record ApiKeyValidationResult(bool IsSuccess, ApiKeyValidationFailure? Failure = null, Guid? TenantId = null, Guid? ApiKeyId = null, string? TenantSlug = null, string? PlanSlug = null, string? CostCenter = null, Autobahn.Domain.Identity.ApiKeyRole? Role = null, string? ApiKeyLabel = null)
{
    public static ApiKeyValidationResult Success(Guid tenantId, Guid apiKeyId, string tenantSlug, string? planSlug, string? costCenter, Autobahn.Domain.Identity.ApiKeyRole role, string? apiKeyLabel = null) => new(true, null, tenantId, apiKeyId, tenantSlug, planSlug, costCenter, role, apiKeyLabel);
    public static ApiKeyValidationResult Fail(ApiKeyValidationFailure failure) => new(false, failure);
}
