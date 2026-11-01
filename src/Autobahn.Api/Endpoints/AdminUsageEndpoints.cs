using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Autobahn.Application.Abstractions;
using Autobahn.Domain.Billing;
using Autobahn.Domain.Models;
using Autobahn.Application.Security;

namespace Autobahn.Api.Endpoints;

public static class AdminUsageEndpoints
{
    /// <summary>
    /// Sentinel accepted in <c>costCenter</c> to select rows that have <em>no</em> cost centre.
    /// </summary>
    public const string NoCostCenterSentinel = "(none)";

    /// <summary>Longest inclusive window one report may span, in days.</summary>
    public const int MaxRangeDays = 366;

    /// <summary>
    /// Window applied when the caller supplies no <c>from</c> date: 30 days inclusive of <c>to</c>.
    /// </summary>
    /// <remarks>
    /// The rollup query has no row limit and both date bounds were optional, so a parameterless call
    /// materialised every rollup row ever written and serialised the lot into one response. A
    /// bounded default keeps the common call cheap; callers wanting more supply an explicit range.
    /// </remarks>
    public const int DefaultUsageWindowDays = 30;

    public static IEndpointRouteBuilder MapAdminUsageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/admin/api/usage")
            .RequireAuthorization(GatewayAuthPolicies.Admin);

        group.MapGet("/", GetUsage);
        group.MapGet("/export", ExportUsage);
        group.MapGet("/forecast", GetForecast);
        group.MapGet("/events", GetEvents);
        group.MapGet("/keys", GetKeyShares);

        // Rewrites stored costs across every tenant, so the operator's alone.
        endpoints.MapPost("/admin/api/usage/reprice", RepriceAsync)
            .RequireAuthorization(GatewayAuthPolicies.Operator);

        return endpoints;
    }

    /// <summary>
    /// Prices a model's stored usage again at its current rate card, for the UTC days
    /// <c>from</c> to <c>to</c>. Without <c>dryRun: false</c> it only reports what would change.
    /// </summary>
    private static async Task<IResult> RepriceAsync(
        HttpContext httpContext,
        IServiceProvider services,
        IAuditLogger audit,
        [Microsoft.AspNetCore.Mvc.FromBody] RepriceBody? body,
        CancellationToken cancellationToken)
    {
        if (services.GetService(typeof(IBillingRepriceService)) is not IBillingRepriceService reprice)
        {
            return Results.Json(
                new { code = "persistence_disabled", message = "Repricing requires ConnectionStrings:GatewayDb." },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (body?.From is not { } from || body.To is not { } to)
        {
            return BadRequest("invalid_range", "'from' and 'to' are required (UTC dates, both included).");
        }

        var dryRun = body.DryRun ?? true;
        try
        {
            var report = await reprice
                .RepriceAsync(new BillingRepriceRequest(body.ModelId ?? string.Empty, from, to, dryRun), cancellationToken)
                .ConfigureAwait(false);

            if (report.Applied)
            {
                audit.LogAdminAction(
                    "usage.reprice",
                    AdminActor.FromHttpContext(httpContext).ToAuditEntry(new
                    {
                        report.ModelId,
                        report.From,
                        report.To,
                        report.Repriced,
                        report.PeakEvents,
                        report.OldTotalUsd,
                        report.NewTotalUsd,
                        RateCardId = report.RateCard.Id,
                    }));
            }

            return Results.Json(report);
        }
        catch (BillingRepriceException ex)
        {
            return Results.Json(new { code = "reprice_refused", message = ex.Message }, statusCode: ex.StatusCode);
        }
    }

    /// <summary>The one filter shape every usage endpoint accepts.</summary>
    /// <remarks>
    /// Bound by hand rather than <c>[AsParameters]</c> so the same validation (range order, range
    /// length, the <c>(none)</c> sentinel) is applied identically on every route.
    /// </remarks>
    private sealed record UsageFilter(
        Guid? TenantId,
        DateOnly From,
        DateOnly To,
        string? CostCenter,
        bool NoCostCenter,
        Guid? ApiKeyId,
        bool IncludeAnonymous,
        string? ModelId)
    {
        public UsageScope Scope => new(TenantId, IncludeAnonymous);

        public UsageReportRequest ToReportRequest() => new()
        {
            FromDate = From,
            ToDate = To,
            TenantId = TenantId,
            IncludeAnonymous = IncludeAnonymous,
            CostCenter = CostCenter,
            NoCostCenter = NoCostCenter,
            ApiKeyId = ApiKeyId,
            ModelId = ModelId,
        };

        public BillingEventQuery ToEventQuery(int limit, BillingEventCursor? cursor) => new(
            From,
            To,
            TenantId,
            ApiKeyId,
            CostCenter,
            limit,
            IncludeAnonymous,
            NoCostCenter,
            cursor,
            ModelId);
    }

    /// <summary>
    /// <c>includeAnonymous</c> is honoured only for callers who satisfy the Operator policy.
    /// </summary>
    /// <remarks>
    /// The group is gated by the per-tenant Admin policy. Anonymous rows (traffic to public models
    /// sent without any key) belong to no tenant, so letting any tenant's admin opt into them
    /// exposed the whole gateway's anonymous request volume, model ids and cost to every tenant.
    /// A non-operator caller asking for them gets a tenant-scoped report, not an error: the flag is
    /// forced to <c>false</c> rather than rejected so existing clients keep working.
    /// <para>
    /// <c>allTenants</c> is the same kind of operator-only widening: the report covers every tenant
    /// rather than the caller's own. Once the keys were moved into a tenant per team, an operator's
    /// report covered only the handful of keys left in the operator tenant, and the usage those
    /// keys recorded before the move stayed under the operator tenant while their later usage was
    /// recorded under the team's — so neither scope alone gave a key's whole history. A tenant named
    /// with <c>X-Autobahn-Tenant</c> wins over it, and for anyone else it is ignored, like
    /// <c>includeAnonymous</c>, so older consoles that never send it see no change.
    /// </para>
    /// </remarks>
    private static async Task<(UsageFilter? Filter, IResult? Error)> TryBindFilterAsync(
        HttpContext httpContext,
        IAuthorizationService authorization,
        DateOnly? from,
        DateOnly? to,
        string? costCenter,
        Guid? apiKeyId,
        bool? includeAnonymous,
        string? modelId,
        bool? allTenants)
    {
        if (!AdminTenantScope.TryResolve(httpContext, out var resolvedTenantId))
        {
            return (null, AdminTenantScope.Denied());
        }

        var isOperator = false;
        if (includeAnonymous == true || allTenants == true)
        {
            var operatorCheck = await authorization
                .AuthorizeAsync(httpContext.User, httpContext, GatewayAuthPolicies.Operator)
                .ConfigureAwait(false);
            isOperator = operatorCheck.Succeeded;
        }

        var anonymousAllowed = includeAnonymous == true && isOperator;
        Guid? tenantId = allTenants == true && isOperator && !AdminTenantScope.IsOverridden(httpContext)
            ? null
            : resolvedTenantId;

        var toDate = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var fromDate = from ?? toDate.AddDays(-(DefaultUsageWindowDays - 1));

        if (fromDate > toDate)
        {
            return (null, BadRequest("invalid_range", "'from' must be on or before 'to'."));
        }

        if (toDate.DayNumber - fromDate.DayNumber + 1 > MaxRangeDays)
        {
            return (null, BadRequest("range_too_long", $"The date range may span at most {MaxRangeDays} days."));
        }

        var trimmed = string.IsNullOrWhiteSpace(costCenter) ? null : costCenter.Trim();
        var noCostCenter = string.Equals(trimmed, NoCostCenterSentinel, StringComparison.OrdinalIgnoreCase);

        return (new UsageFilter(
            tenantId,
            fromDate,
            toDate,
            noCostCenter ? null : trimmed,
            noCostCenter,
            apiKeyId,
            anonymousAllowed,
            string.IsNullOrWhiteSpace(modelId) ? null : modelId.Trim()), null);
    }

    private static async Task<IResult> GetUsage(
        HttpContext httpContext,
        IAuthorizationService authorization,
        IBillingUsageService usageService,
        DateOnly? from,
        DateOnly? to,
        string? costCenter,
        Guid? apiKeyId,
        bool? includeAnonymous,
        string? modelId,
        bool? allTenants,
        CancellationToken cancellationToken)
    {
        var (filter, error) = await TryBindFilterAsync(httpContext, authorization, from, to, costCenter, apiKeyId, includeAnonymous, modelId, allTenants)
            .ConfigureAwait(false);
        if (filter is null)
        {
            return error!;
        }

        var report = await usageService
            .GetUsageReportAsync(filter.ToReportRequest(), cancellationToken)
            .ConfigureAwait(false);

        return Results.Json(report);
    }

    private static async Task<IResult> GetForecast(
        HttpContext httpContext,
        IAuthorizationService authorization,
        IBillingForecastService forecastService,
        int? days,
        string? costCenter,
        Guid? apiKeyId,
        bool? includeAnonymous,
        string? modelId,
        bool? allTenants,
        CancellationToken cancellationToken)
    {
        // The forecast has its own window (trailing complete days + month to date), so the report's
        // from/to are deliberately not accepted here; the other filters are shared.
        var (filter, error) = await TryBindFilterAsync(httpContext, authorization, null, null, costCenter, apiKeyId, includeAnonymous, modelId, allTenants)
            .ConfigureAwait(false);
        if (filter is null)
        {
            return error!;
        }

        var report = await forecastService
            .GetForecastAsync(
                new UsageForecastRequest
                {
                    Scope = filter.Scope,
                    CostCenter = filter.CostCenter,
                    NoCostCenter = filter.NoCostCenter,
                    ApiKeyId = filter.ApiKeyId,
                    ModelId = filter.ModelId,
                    TrailingDays = days ?? 7,
                },
                cancellationToken)
            .ConfigureAwait(false);

        return Results.Json(report);
    }

    private static async Task<IResult> GetEvents(
        HttpContext httpContext,
        IAuthorizationService authorization,
        IBillingUsageService usageService,
        DateOnly? from,
        DateOnly? to,
        Guid? apiKeyId,
        string? costCenter,
        bool? includeAnonymous,
        string? modelId,
        bool? allTenants,
        int? limit,
        string? cursor,
        CancellationToken cancellationToken)
    {
        var (filter, error) = await TryBindFilterAsync(httpContext, authorization, from, to, costCenter, apiKeyId, includeAnonymous, modelId, allTenants)
            .ConfigureAwait(false);
        if (filter is null)
        {
            return error!;
        }

        BillingEventCursor? decoded = null;
        if (!string.IsNullOrWhiteSpace(cursor) && !BillingEventCursor.TryDecode(cursor, out decoded))
        {
            return BadRequest("invalid_cursor", "The 'cursor' value is not a cursor issued by this endpoint.");
        }

        var page = await usageService
            .QueryEventsAsync(filter.ToEventQuery(limit ?? 100, decoded), cancellationToken)
            .ConfigureAwait(false);

        return Results.Json(page);
    }

    /// <summary>Each key's share of the filtered load; usually read with <c>modelId</c> set.</summary>
    private static async Task<IResult> GetKeyShares(
        HttpContext httpContext,
        IAuthorizationService authorization,
        IBillingUsageService usageService,
        DateOnly? from,
        DateOnly? to,
        string? costCenter,
        Guid? apiKeyId,
        bool? includeAnonymous,
        string? modelId,
        bool? allTenants,
        CancellationToken cancellationToken)
    {
        var (filter, error) = await TryBindFilterAsync(httpContext, authorization, from, to, costCenter, apiKeyId, includeAnonymous, modelId, allTenants)
            .ConfigureAwait(false);
        if (filter is null)
        {
            return error!;
        }

        var shares = await usageService
            .GetKeySharesAsync(filter.ToEventQuery(UsageExportLimits.MaxEventPageSize, null), cancellationToken)
            .ConfigureAwait(false);

        return Results.Json(shares);
    }

    private static async Task<IResult> ExportUsage(
        HttpContext httpContext,
        IAuthorizationService authorization,
        IBillingUsageService usageService,
        DateOnly? from,
        DateOnly? to,
        string? costCenter,
        Guid? apiKeyId,
        bool? includeAnonymous,
        string? modelId,
        bool? allTenants,
        string? format,
        string? dataset,
        CancellationToken cancellationToken)
    {
        var (filter, error) = await TryBindFilterAsync(httpContext, authorization, from, to, costCenter, apiKeyId, includeAnonymous, modelId, allTenants)
            .ConfigureAwait(false);
        if (filter is null)
        {
            return error!;
        }

        var fmt = (format ?? "json").Trim().ToLowerInvariant();
        if (fmt is not ("json" or "csv"))
        {
            return BadRequest("invalid_format", "'format' must be 'json' or 'csv'.");
        }

        var set = (dataset ?? "rollups").Trim().ToLowerInvariant();
        UsageExportResult export;
        switch (set)
        {
            case "rollups":
                var report = await usageService
                    .GetUsageReportAsync(filter.ToReportRequest(), cancellationToken)
                    .ConfigureAwait(false);
                export = usageService.ExportRollups(report.Rollups, fmt);
                break;
            case "events":
                export = await usageService
                    .ExportEventsAsync(filter.ToEventQuery(UsageExportLimits.MaxEventRows, null), fmt, cancellationToken)
                    .ConfigureAwait(false);
                break;
            default:
                return BadRequest("invalid_dataset", "'dataset' must be 'rollups' or 'events'.");
        }

        httpContext.Response.Headers["X-Export-Truncated"] = export.Truncated ? "true" : "false";
        httpContext.Response.Headers["Access-Control-Expose-Headers"] = "Content-Disposition, X-Export-Truncated";
        return Results.File(
            System.Text.Encoding.UTF8.GetBytes(export.Body),
            export.ContentType,
            export.FileName);
    }

    private static IResult BadRequest(string code, string message) =>
        Results.Json(new { code, message }, statusCode: StatusCodes.Status400BadRequest);
}
