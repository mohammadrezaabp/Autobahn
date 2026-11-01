using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Autobahn.Api;
using Autobahn.Api.Contracts;
using Autobahn.Api.Services;
using Autobahn.Application.Abstractions;
using Autobahn.Domain.Models;
using Autobahn.Application.Security;

namespace Autobahn.Api.Endpoints;

public static class AdminControlPlaneEndpoints
{
    public static IEndpointRouteBuilder MapAdminControlPlaneEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/admin/api")
            .RequireAuthorization(GatewayAuthPolicies.Operator);

        group.MapGet("/summary", GetSummary);
        group.MapGet("/backends", ListBackends);
        group.MapGet("/models", ListModels);
        group.MapGet("/model-types", GetModelTypes);
        group.MapGet("/requests", ListRequests);
        group.MapGet("/live", StreamLive);
        group.MapPost("/models", AddModel);
        group.MapPatch("/models/{id}", UpdateModel);
        group.MapDelete("/models/{id}", RemoveModel);
        group.MapPost("/models/{id}/stop", StopModel);
        group.MapPost("/models/{id}/start", StartModel);
        group.MapPost("/models/{id}/test", TestModel);
        group.MapPost("/models/{id}/backends", AddBackend);
        group.MapPatch("/models/{id}/backends/{name}", UpdateBackend);
        group.MapDelete("/models/{id}/backends/{name}", RemoveBackend);
        group.MapPost("/models/{id}/backends/{name}/stop", StopBackend);
        group.MapPost("/models/{id}/backends/{name}/start", StartBackend);
        group.MapPost("/models/{id}/backends/{name}/test", TestBackend);
        group.MapGet("/logs", ListLogs);
        group.MapDelete("/logs", ClearLogs);

        return endpoints;
    }

    private static IResult GetSummary(IControlPlaneCommands commands) =>
        Results.Json(commands.GetSummary());

    private static IResult ListBackends(IControlPlaneCommands commands) =>
        Results.Json(commands.ListBackends());

    private static async Task<IResult> ListModels(
        AdminModelProvisioningService provisioning,
        CancellationToken cancellationToken) =>
        Results.Json(await provisioning.ListModelsAsync(cancellationToken).ConfigureAwait(false));

    /// <summary>
    /// The canonical model-type taxonomy, so the admin UI does not keep its own copy. The UI's
    /// hand-maintained duplicate had drifted to a fraction of the accepted aliases, which made
    /// correctly-typed models display as text generation and silently be rewritten on save.
    /// </summary>
    private static IResult GetModelTypes() =>
        Results.Json(AdminModelTypeDescriptor.All());

    private static IResult ListRequests(IControlPlaneCommands commands, int? limit) =>
        Results.Json(commands.ListRecentRequests(limit is > 0 and <= 500 ? limit.Value : 50));

    private static readonly JsonSerializerOptions LiveJson = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The Overview's push channel: a server-sent-event stream that emits a
    /// <c>{ version, summary, requests }</c> frame whenever the gateway's activity changes, so a
    /// request appears the moment it is admitted and its cost the moment it is priced — not on the
    /// next 2-second poll. Frames are coalesced to at most one every <see cref="LiveMinInterval"/>;
    /// a comment heartbeat keeps intermediaries from closing an idle connection.
    /// </summary>
    /// <remarks>
    /// Polling is not removed: the console falls back to it when the stream cannot be established
    /// (a proxy that buffers, a browser that lost the connection), so this endpoint is an upgrade,
    /// not a dependency.
    /// </remarks>
    private static async Task StreamLive(
        HttpContext httpContext,
        IControlPlaneCommands commands,
        IAdminLiveFeed feed,
        ILoggerFactory loggerFactory,
        int? limit,
        CancellationToken cancellationToken)
    {
        var take = limit is > 0 and <= 500 ? limit.Value : 50;
        var response = httpContext.Response;
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache, no-store";
        response.Headers.Connection = "keep-alive";
        // Tells nginx-style proxies not to buffer the stream.
        response.Headers["X-Accel-Buffering"] = "no";
        httpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        long? sent = null;
        var lastWrite = DateTimeOffset.UtcNow;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var version = feed.Version;
                var now = DateTimeOffset.UtcNow;

                if (sent != version)
                {
                    var frame = new
                    {
                        version,
                        summary = commands.GetSummary(),
                        requests = commands.ListRecentRequests(take),
                    };

                    await response.WriteAsync("event: update\ndata: ", cancellationToken).ConfigureAwait(false);
                    await response.WriteAsync(JsonSerializer.Serialize(frame, LiveJson), cancellationToken).ConfigureAwait(false);
                    await response.WriteAsync("\n\n", cancellationToken).ConfigureAwait(false);
                    await response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
                    sent = version;
                    lastWrite = now;
                }
                else if (now - lastWrite >= LiveHeartbeat)
                {
                    await response.WriteAsync(": ping\n\n", cancellationToken).ConfigureAwait(false);
                    await response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
                    lastWrite = now;
                }

                await Task.Delay(LiveMinInterval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The browser navigated away or the tab closed; nothing to report.
        }
        catch (Exception ex)
        {
            // The response is long since committed, so this cannot become an error page. Letting it
            // escape would reach the terminal handler, be recorded as a gateway error, and — because
            // the console reconnects — repeat for as long as the fault persists, burying real faults
            // in the Errors tab. Close the stream instead and let the client fall back to polling.
            loggerFactory
                .CreateLogger(typeof(AdminControlPlaneEndpoints).FullName!)
                .LogWarning(ex, "Admin live stream ended early; the console will fall back to polling.");
        }
    }

    private static readonly TimeSpan LiveMinInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan LiveHeartbeat = TimeSpan.FromSeconds(15);

    // Model writes are audited here, with the caller's identity, like every other admin mutation.
    // Changing a model's upstream URL redirects tenant traffic (and the upstream credential) to a
    // new host, and deleting one takes it out of service; neither left a trace before.
    private static async Task<IResult> AddModel(
        HttpContext httpContext,
        AdminModelProvisioningService provisioning,
        IAuditLogger audit,
        [FromBody] AdminModelWriteRequest? request,
        CancellationToken cancellationToken)
    {
        if (request?.Model is null)
        {
            return Results.BadRequest(new { message = "Request body must include model." });
        }

        var actor = AdminActor.FromHttpContext(httpContext);
        var result = await provisioning.AddAsync(request, actor, cancellationToken).ConfigureAwait(false);
        if (result.Success)
        {
            audit.LogAdminAction(
                "model.create",
                actor.ToAuditEntry(DescribeModelWrite(request.Model)));
        }

        return Results.Json(result, statusCode: result.SuggestedStatusCode);
    }

    private static async Task<IResult> UpdateModel(
        HttpContext httpContext,
        AdminModelProvisioningService provisioning,
        IAuditLogger audit,
        string id,
        [FromBody] AdminModelWriteRequest? request,
        CancellationToken cancellationToken)
    {
        if (request?.Model is null)
        {
            return Results.BadRequest(new { message = "Request body must include model." });
        }

        var actor = AdminActor.FromHttpContext(httpContext);
        var routeId = AdminModelRouteId.Decode(id);
        var result = await provisioning.UpdateAsync(routeId, request, actor, cancellationToken).ConfigureAwait(false);
        if (result.Success)
        {
            audit.LogAdminAction(
                "model.update",
                actor.ToAuditEntry(DescribeModelWrite(request.Model, routeId)));
        }

        return Results.Json(result, statusCode: result.SuggestedStatusCode);
    }

    private static async Task<IResult> RemoveModel(
        HttpContext httpContext,
        IControlPlaneCommands commands,
        IRateCardAdminService pricing,
        IAuditLogger audit,
        string id,
        CancellationToken cancellationToken)
    {
        var modelId = AdminModelRouteId.Decode(id);
        var result = await commands.RemoveModelAsync(modelId, cancellationToken).ConfigureAwait(false);

        if (result.Success)
        {
            // Drop pricing too, so a model later re-created under the same id does not
            // silently inherit a stale rate card.
            await pricing.ClearPricingAsync(modelId, cancellationToken).ConfigureAwait(false);

            audit.LogAdminAction(
                "model.delete",
                AdminActor.FromHttpContext(httpContext).ToAuditEntry(new { modelId }));
        }

        return Results.Json(result, statusCode: result.SuggestedStatusCode);
    }

    /// <summary>
    /// Takes a route out of service: it stays registered — with its aliases, credential, pricing and
    /// grants — but disappears from <c>GET /v1/models</c> and its inference requests are refused.
    /// </summary>
    /// <remarks>
    /// Separate from <c>DELETE</c> because it is the reversible half of the same operator intent
    /// ("stop sending traffic here"), and separate from <c>PATCH</c> because it must not carry, and
    /// therefore must not overwrite, the rest of the model.
    /// </remarks>
    private static Task<IResult> StopModel(
        HttpContext httpContext,
        IControlPlaneCommands commands,
        IAuditLogger audit,
        string id,
        CancellationToken cancellationToken) =>
        SetModelStateAsync(httpContext, commands, audit, id, ModelRouteStates.Stopped, cancellationToken);

    /// <summary>Puts a stopped route back in service.</summary>
    private static Task<IResult> StartModel(
        HttpContext httpContext,
        IControlPlaneCommands commands,
        IAuditLogger audit,
        string id,
        CancellationToken cancellationToken) =>
        SetModelStateAsync(httpContext, commands, audit, id, ModelRouteStates.Serving, cancellationToken);

    private static async Task<IResult> SetModelStateAsync(
        HttpContext httpContext,
        IControlPlaneCommands commands,
        IAuditLogger audit,
        string id,
        string state,
        CancellationToken cancellationToken)
    {
        var modelId = AdminModelRouteId.Decode(id);
        var result = await commands.SetModelStateAsync(modelId, state, cancellationToken).ConfigureAwait(false);

        if (result.Success)
        {
            // Audited like every other model mutation: taking a model out of service changes what
            // the whole gateway will serve, and an operator investigating "why did this model stop
            // answering" needs to find who did it and when.
            audit.LogAdminAction(
                state == ModelRouteStates.Stopped ? "model.stop" : "model.start",
                AdminActor.FromHttpContext(httpContext).ToAuditEntry(new { modelId, state }));
        }

        return Results.Json(result, statusCode: result.SuggestedStatusCode);
    }

    /// <summary>
    /// The audit-worthy shape of a model write: identity, upstream, and routing surface. Never the
    /// request's <c>apiKey</c> — the credential is audited separately as presence, not value.
    /// </summary>
    private static object DescribeModelWrite(ModelConfig model, string? routeId = null) => new
    {
        routeId,
        modelId = model.Id,
        url = model.Url,
        backends = model.Backends?.Select(b => new { name = b.Name, url = b.Url }),
        modelType = model.ModelType,
        provider = model.Provider,
        state = model.State,
        aliases = model.Aliases,
        publicAccess = model.PublicAccess,
        upstreamAuth = model.UpstreamAuth is null
            ? null
            : new { type = model.UpstreamAuth.Type, envVar = model.UpstreamAuth.EnvVar, secretRef = model.UpstreamAuth.SecretRef },
    };

    /// <summary>
    /// Recent gateway diagnostics, newest first. <paramref name="level"/> is a floor
    /// (<c>warning</c> hides info), <paramref name="search"/> a case-insensitive substring.
    /// </summary>
    private static IResult ListLogs(
        IGatewayLogStore logs,
        int? limit,
        string? level,
        string? search)
    {
        var take = limit is > 0 and <= 500 ? limit.Value : 100;

        // Fetch the whole matching set and slice, so the response can report how much the limit hid.
        // The buffer is bounded at a few hundred entries, so this is one cheap pass.
        var matched = logs.GetRecent(logs.Capacity, GatewayLogLevels.ParseFilter(level), search);
        var entries = matched.Count <= take ? matched : matched.Take(take).ToList();

        return Results.Json(new AdminLogListResponse
        {
            Entries = [.. entries.Select(AdminLogEntryDto.From)],
            Count = entries.Count,
            Total = matched.Count,
            Capacity = logs.Capacity,
        });
    }

    private static IResult ClearLogs(HttpContext httpContext, IGatewayLogStore logs, IAuditLogger audit)
    {
        var cleared = logs.Clear();

        // Wiping the diagnostic buffer is a mutation an operator can perform mid-incident; it needs
        // to leave a trace like every other admin mutation does.
        audit.LogAdminAction(
            "logs.clear",
            new AuditLogEntry(
                httpContext.User.FindFirst(GatewayAuthClaims.TenantId)?.Value,
                httpContext.User.FindFirst(GatewayAuthClaims.ApiKeyId)?.Value,
                new { entriesCleared = cleared }));

        return Results.Json(new { success = true, message = "Log buffer cleared.", entriesCleared = cleared });
    }

    private static async Task<IResult> TestModel(
        AdminModelTestService testService,
        string id,
        [FromBody] AdminModelTestRequest? request,
        CancellationToken cancellationToken)
    {
        var result = await testService.TestAsync(AdminModelRouteId.Decode(id), request, cancellationToken).ConfigureAwait(false);
        return Results.Json(result, statusCode: result.SuggestedStatusCode);
    }

    // One backend of a model, written on its own. Each is a separate mutation for the reason stop
    // and start are: a whole-model PATCH carries the whole list, so two callers each adding a
    // backend (two rollouts, or a rollout and an operator in the console) would overwrite one another.
    private static async Task<IResult> AddBackend(
        HttpContext httpContext,
        AdminModelProvisioningService provisioning,
        IAuditLogger audit,
        string id,
        [FromBody] AdminBackendWriteRequest? request,
        CancellationToken cancellationToken)
    {
        if (request?.Backend is null)
        {
            return Results.BadRequest(new { message = "Request body must include backend." });
        }

        var actor = AdminActor.FromHttpContext(httpContext);
        var modelId = AdminModelRouteId.Decode(id);
        var result = await provisioning.AddBackendAsync(modelId, request, actor, cancellationToken).ConfigureAwait(false);
        if (result.Success)
        {
            audit.LogAdminAction(
                "model.backend.create",
                actor.ToAuditEntry(DescribeBackendWrite(modelId, request.Backend.Name, request.Backend)));
        }

        return Results.Json(result, statusCode: result.SuggestedStatusCode);
    }

    private static async Task<IResult> UpdateBackend(
        HttpContext httpContext,
        AdminModelProvisioningService provisioning,
        IAuditLogger audit,
        string id,
        string name,
        [FromBody] AdminBackendWriteRequest? request,
        CancellationToken cancellationToken)
    {
        if (request?.Backend is null)
        {
            return Results.BadRequest(new { message = "Request body must include backend." });
        }

        var actor = AdminActor.FromHttpContext(httpContext);
        var modelId = AdminModelRouteId.Decode(id);
        var result = await provisioning.UpdateBackendAsync(modelId, name, request, actor, cancellationToken)
            .ConfigureAwait(false);
        if (result.Success)
        {
            audit.LogAdminAction(
                "model.backend.update",
                actor.ToAuditEntry(DescribeBackendWrite(modelId, name, request.Backend)));
        }

        return Results.Json(result, statusCode: result.SuggestedStatusCode);
    }

    private static async Task<IResult> RemoveBackend(
        HttpContext httpContext,
        IControlPlaneCommands commands,
        IAuditLogger audit,
        string id,
        string name,
        CancellationToken cancellationToken)
    {
        var modelId = AdminModelRouteId.Decode(id);
        var result = await commands.RemoveBackendAsync(modelId, name, cancellationToken).ConfigureAwait(false);
        if (result.Success)
        {
            audit.LogAdminAction(
                "model.backend.delete",
                AdminActor.FromHttpContext(httpContext).ToAuditEntry(new { modelId, backend = name }));
        }

        return Results.Json(result, statusCode: result.SuggestedStatusCode);
    }

    /// <summary>
    /// Takes one backend out of the choice for new requests. The requests it is already answering
    /// finish; <c>GET /admin/api/backends</c> reports its <c>inFlight</c>, which reaches 0 when the
    /// backend can be taken down.
    /// </summary>
    private static Task<IResult> StopBackend(
        HttpContext httpContext,
        IControlPlaneCommands commands,
        IAuditLogger audit,
        string id,
        string name,
        CancellationToken cancellationToken) =>
        SetBackendStateAsync(httpContext, commands, audit, id, name, ModelRouteStates.Stopped, cancellationToken);

    private static Task<IResult> StartBackend(
        HttpContext httpContext,
        IControlPlaneCommands commands,
        IAuditLogger audit,
        string id,
        string name,
        CancellationToken cancellationToken) =>
        SetBackendStateAsync(httpContext, commands, audit, id, name, ModelRouteStates.Serving, cancellationToken);

    private static async Task<IResult> SetBackendStateAsync(
        HttpContext httpContext,
        IControlPlaneCommands commands,
        IAuditLogger audit,
        string id,
        string name,
        string state,
        CancellationToken cancellationToken)
    {
        var modelId = AdminModelRouteId.Decode(id);
        var result = await commands.SetBackendStateAsync(modelId, name, state, cancellationToken).ConfigureAwait(false);
        if (result.Success)
        {
            audit.LogAdminAction(
                state == ModelRouteStates.Stopped ? "model.backend.stop" : "model.backend.start",
                AdminActor.FromHttpContext(httpContext).ToAuditEntry(new { modelId, backend = name, state }));
        }

        return Results.Json(result, statusCode: result.SuggestedStatusCode);
    }

    private static async Task<IResult> TestBackend(
        AdminModelTestService testService,
        string id,
        string name,
        [FromBody] AdminModelTestRequest? request,
        CancellationToken cancellationToken)
    {
        var result = await testService
            .TestAsync(AdminModelRouteId.Decode(id), name, request, cancellationToken)
            .ConfigureAwait(false);
        return Results.Json(result, statusCode: result.SuggestedStatusCode);
    }

    /// <summary>As <see cref="DescribeModelWrite"/>: where the backend is, never its <c>apiKey</c>.</summary>
    private static object DescribeBackendWrite(string modelId, string name, ModelBackendConfig backend) => new
    {
        modelId,
        backend = name,
        url = backend.Url,
        maxContextLength = backend.MaxContextLength,
        priority = backend.Priority,
        upstreamModel = backend.UpstreamModel,
        labels = backend.Labels,
    };
}
