using System.Text.RegularExpressions;
using Autobahn.Application.Abstractions;
using Autobahn.Domain.Identity;
using Autobahn.Domain.Models;
using Autobahn.Security.Configuration;

namespace Autobahn.Security.Services;

public sealed partial class TenantAdminService : ITenantAdminService
{
    /// <summary>
    /// Bounds one move request. Each key costs a handful of round trips, and a batch is not atomic,
    /// so a ceiling keeps a mistaken "move everything" both quick and easy to read back.
    /// </summary>
    public const int MaxKeysPerMove = 500;

    private const int MaxSlugLength = 64;
    private const int MaxNameLength = 256;
    private const int MaxPlanSlugLength = 64;
    private const int MaxCostCenterLength = 128;

    private readonly ITenantRepository _tenants;
    private readonly IApiKeyRepository _apiKeys;
    private readonly IApiKeyLifecycleEventRepository _lifecycle;
    private readonly IApiKeyValidator _validator;
    private readonly OperatorTenantConfiguration _operatorTenant;
    private readonly IGatewayConfigProvider? _config;

    public TenantAdminService(
        ITenantRepository tenants,
        IApiKeyRepository apiKeys,
        IApiKeyLifecycleEventRepository lifecycle,
        IApiKeyValidator validator,
        OperatorTenantConfiguration operatorTenant,
        IGatewayConfigProvider? config = null)
    {
        _tenants = tenants;
        _apiKeys = apiKeys;
        _lifecycle = lifecycle;
        _validator = validator;
        _operatorTenant = operatorTenant;
        // Optional so the service can be built without a config snapshot; absent, a plan slug is
        // checked for shape only.
        _config = config;
    }

    public async Task<IReadOnlyList<AdminTenantListItem>> ListAsync(CancellationToken cancellationToken = default)
    {
        var tenants = await _tenants.ListAllAsync(cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;

        var items = new List<AdminTenantListItem>(tenants.Count);
        foreach (var tenant in tenants)
        {
            var keys = await _apiKeys
                .ListByTenantAsync(tenant.Id, includeArchived: false, cancellationToken)
                .ConfigureAwait(false);
            items.Add(ToListItem(tenant, keys.Count, keys.Count(k => CanAuthenticate(k, now))));
        }

        return items;
    }

    public async Task<AdminTenantListItem> CreateAsync(
        CreateAdminTenantRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var slug = NormalizeSlug(request.Slug);
        var name = NormalizeName(request.Name);
        var planSlug = NormalizeOptional(request.PlanSlug, MaxPlanSlugLength, "planSlug");
        var costCenter = NormalizeOptional(request.CostCenter, MaxCostCenterLength, "costCenter");
        RequireKnownPlan(planSlug);

        // The unique index on the slug is what finally decides a race between two creates; this
        // read is what turns the ordinary case into a 409 with a reason instead of a store error.
        if (await _tenants.GetBySlugAsync(slug, cancellationToken).ConfigureAwait(false) is not null)
        {
            throw new TenantSlugConflictException(slug);
        }

        var now = DateTimeOffset.UtcNow;
        var created = await _tenants.CreateAsync(
            new TenantRecord(Guid.NewGuid(), slug, name, planSlug, costCenter, IsActive: true, now, now),
            cancellationToken).ConfigureAwait(false);

        return ToListItem(created, keyCount: 0, activeKeyCount: 0);
    }

    public async Task<AdminTenantListItem> UpdateAsync(
        Guid tenantId,
        UpdateAdminTenantRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var existing = await RequireTenantAsync(tenantId, cancellationToken).ConfigureAwait(false);

        var name = NormalizeName(request.Name);
        var planSlug = NormalizeOptional(request.PlanSlug, MaxPlanSlugLength, "planSlug");
        var costCenter = NormalizeOptional(request.CostCenter, MaxCostCenterLength, "costCenter");

        // Only a plan that is being changed is checked. A tenant may already carry a plan that was
        // since removed from the configuration, and renaming that tenant must not be refused over it.
        if (!string.Equals(planSlug, existing.PlanSlug, StringComparison.Ordinal))
        {
            RequireKnownPlan(planSlug);
        }

        var updated = await _tenants
            .UpdateAsync(tenantId, name, planSlug, costCenter, DateTimeOffset.UtcNow, cancellationToken)
            .ConfigureAwait(false);

        // A validated key caches its tenant's plan and cost center. Without this the old plan's
        // rate limit would keep applying for up to the cache TTL after the change was confirmed.
        var keys = await _apiKeys
            .ListByTenantAsync(tenantId, includeArchived: false, cancellationToken)
            .ConfigureAwait(false);
        foreach (var key in keys)
        {
            _validator.InvalidateCache(key.Id);
        }

        var now = DateTimeOffset.UtcNow;
        return ToListItem(updated, keys.Count, keys.Count(k => CanAuthenticate(k, now)));
    }

    public async Task<MoveAdminApiKeysResponse> MoveKeysAsync(
        Guid targetTenantId,
        IReadOnlyCollection<Guid> keyIds,
        Guid? actorKeyId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keyIds);

        var distinctIds = keyIds.Where(static id => id != Guid.Empty).Distinct().ToArray();
        if (distinctIds.Length == 0)
        {
            throw new ArgumentException("At least one key id is required.", nameof(keyIds));
        }

        if (distinctIds.Length > MaxKeysPerMove)
        {
            throw new ArgumentException(
                $"At most {MaxKeysPerMove} keys can be moved in one request.",
                nameof(keyIds));
        }

        var target = await RequireTenantAsync(targetTenantId, cancellationToken).ConfigureAwait(false);
        if (!target.IsActive)
        {
            // Every key of an inactive tenant is refused at authentication, so this move would
            // switch the keys off while reading as a reorganisation.
            throw new TenantInactiveException(target.Slug);
        }

        var moved = new List<MovedAdminApiKey>();
        var skipped = new List<SkippedAdminApiKey>();
        var sourceSlugs = new Dictionary<Guid, string?>();

        foreach (var keyId in distinctIds)
        {
            var record = await _apiKeys.GetByIdAsync(keyId, cancellationToken).ConfigureAwait(false);
            if (record is null)
            {
                skipped.Add(Skip(keyId, MoveApiKeySkipCodes.NotFound, "The key does not exist."));
                continue;
            }

            if (record.TenantId == target.Id)
            {
                skipped.Add(Skip(
                    keyId,
                    MoveApiKeySkipCodes.AlreadyInTenant,
                    $"The key already belongs to tenant '{target.Slug}'."));
                continue;
            }

            // Moving the caller's own key would change what the rest of this very session is
            // allowed to do, and out of the operator tenant it would end it.
            if (record.Id == actorKeyId)
            {
                skipped.Add(Skip(
                    keyId,
                    MoveApiKeySkipCodes.SelfAction,
                    "The key used for this request cannot move itself."));
                continue;
            }

            var sourceTenantId = record.TenantId;
            var now = DateTimeOffset.UtcNow;
            var isActiveAdmin = IsAdminRole(record.Role) && CanAuthenticate(record, now);

            if (isActiveAdmin &&
                await _apiKeys.CountActiveAdminKeysAsync(sourceTenantId, cancellationToken).ConfigureAwait(false) <= 1)
            {
                skipped.Add(LastAdminKey(keyId));
                continue;
            }

            await _apiKeys.MoveToTenantAsync(keyId, target.Id, cancellationToken).ConfigureAwait(false);

            // Same recheck as a revoke, for the same reason: the count above and the write are two
            // statements, so two operators moving a tenant's last two admin keys at once can both
            // pass it. Whoever now sees zero puts the key back.
            if (isActiveAdmin &&
                await _apiKeys.CountActiveAdminKeysAsync(sourceTenantId, cancellationToken).ConfigureAwait(false) == 0)
            {
                await _apiKeys.MoveToTenantAsync(keyId, sourceTenantId, cancellationToken).ConfigureAwait(false);
                _validator.InvalidateCache(keyId);
                skipped.Add(LastAdminKey(keyId));
                continue;
            }

            // The cached validation result carries the old tenant; dropping it is what makes the
            // next request land in the new tenant's bucket rather than a cache TTL later.
            _validator.InvalidateCache(keyId);

            // One event under each tenant. Lifecycle history is read by (tenant, key), so a single
            // row would leave one of the two tenants with a key that appears or vanishes unexplained.
            await _lifecycle.AppendAsync(
                NewEvent(record, sourceTenantId, ApiKeyLifecycleEvent.MovedOut, now, actorKeyId),
                cancellationToken).ConfigureAwait(false);
            await _lifecycle.AppendAsync(
                NewEvent(record, target.Id, ApiKeyLifecycleEvent.MovedIn, now, actorKeyId),
                cancellationToken).ConfigureAwait(false);

            if (!sourceSlugs.TryGetValue(sourceTenantId, out var sourceSlug))
            {
                sourceSlug = (await _tenants.GetByIdAsync(sourceTenantId, cancellationToken).ConfigureAwait(false))?.Slug;
                sourceSlugs[sourceTenantId] = sourceSlug;
            }

            moved.Add(new MovedAdminApiKey
            {
                Id = record.Id,
                KeyPrefix = record.KeyPrefix,
                Label = record.Label,
                FromTenantId = sourceTenantId,
                FromTenantSlug = sourceSlug,
            });
        }

        return new MoveAdminApiKeysResponse
        {
            TenantId = target.Id,
            TenantSlug = target.Slug,
            Moved = moved,
            Skipped = skipped,
        };
    }

    private async Task<TenantRecord> RequireTenantAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await _tenants.GetByIdAsync(tenantId, cancellationToken).ConfigureAwait(false)
        ?? throw new KeyNotFoundException($"Tenant '{tenantId}' was not found.");

    private void RequireKnownPlan(string? planSlug)
    {
        if (planSlug is null || _config is null)
        {
            return;
        }

        // An unknown plan is not an error at admission: the tenant silently gets the default tier.
        // Refusing it here is the only place the typo is ever reported.
        var plans = _config.Current.RateLimits.Plans;
        if (!plans.ContainsKey(planSlug))
        {
            var known = plans.Count == 0
                ? "No plans are configured"
                : $"Configured plans: {string.Join(", ", plans.Keys.Order(StringComparer.Ordinal))}";
            throw new ArgumentException(
                $"planSlug '{planSlug}' is not a configured rate-limit plan. {known}. "
                + "Omit planSlug to use the default tier.",
                nameof(planSlug));
        }
    }

    private AdminTenantListItem ToListItem(TenantRecord tenant, int keyCount, int activeKeyCount) =>
        new()
        {
            Id = tenant.Id,
            Slug = tenant.Slug,
            Name = tenant.Name,
            PlanSlug = tenant.PlanSlug,
            CostCenter = tenant.CostCenter,
            IsActive = tenant.IsActive,
            IsOperatorTenant = _operatorTenant.IsOperatorTenant(tenant.Slug),
            CreatedAt = tenant.CreatedAt,
            UpdatedAt = tenant.UpdatedAt,
            KeyCount = keyCount,
            ActiveKeyCount = activeKeyCount,
        };

    private static string NormalizeSlug(string? value)
    {
        var slug = value?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(slug) || slug.Length > MaxSlugLength || !SlugPattern().IsMatch(slug))
        {
            throw new ArgumentException(
                $"slug must be 1 to {MaxSlugLength} characters of lowercase letters, digits and "
                + "hyphens, starting and ending with a letter or digit.",
                nameof(value));
        }

        return slug;
    }

    private static string NormalizeName(string? value)
    {
        var name = value?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength)
        {
            throw new ArgumentException($"name is required, at most {MaxNameLength} characters.", nameof(value));
        }

        return name;
    }

    private static string? NormalizeOptional(string? value, int maxLength, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentException($"{field} must be at most {maxLength} characters.", field);
        }

        return trimmed;
    }

    private static bool CanAuthenticate(ApiKeyRecord key, DateTimeOffset now) =>
        key.RevokedAt is null &&
        key.ArchivedAt is null &&
        (key.ExpiresAt is null || key.ExpiresAt > now);

    private static bool IsAdminRole(ApiKeyRole role) => role is ApiKeyRole.Admin or ApiKeyRole.Both;

    private static SkippedAdminApiKey Skip(Guid keyId, string code, string message) =>
        new() { Id = keyId, Code = code, Message = message };

    private static SkippedAdminApiKey LastAdminKey(Guid keyId) =>
        Skip(
            keyId,
            MoveApiKeySkipCodes.LastAdminKey,
            "The key is its tenant's last active admin key; create another admin key there first.");

    private static ApiKeyLifecycleEventRecord NewEvent(
        ApiKeyRecord record,
        Guid tenantId,
        ApiKeyLifecycleEvent lifecycleEvent,
        DateTimeOffset occurredAt,
        Guid? actorKeyId) =>
        new(
            Guid.NewGuid(),
            record.Id,
            tenantId,
            record.KeyPrefix,
            lifecycleEvent,
            occurredAt,
            record.Label,
            actorKeyId,
            HadUsage: record.LastUsedAt is not null);

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern();
}
