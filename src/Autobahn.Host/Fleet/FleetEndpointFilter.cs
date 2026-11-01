using System.Text.Json;
using Autobahn.Application.Fleet;
using Autobahn.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace Autobahn.Host.Fleet;

public sealed class FleetEndpointFilter(FleetSessionService sessions) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        http.Response.Headers.CacheControl = "no-store";
        try
        {
            var write = !HttpMethods.IsGet(http.Request.Method);
            if (write && http.Request.Headers[FleetSessionService.RequestHeader] != "1")
                throw new FleetException(400, "request_header_required", "The Autobahn request header is required.");
            // Only the registered sign-in endpoint carries this server-owned metadata.
            var endpoint = http.GetEndpoint();
            var login = endpoint?.Metadata.GetMetadata<IAllowAnonymous>() is not null;
            if (!login)
            {
                var identity = await sessions.AuthenticateAsync(http, http.RequestAborted);
                http.Items["FleetActor"] = identity.Actor; http.Items["FleetCanWrite"] = identity.CanWrite; http.Items["FleetExpires"] = identity.ExpiresAt;
                var logout = endpoint?.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == "FleetSessionSignOut";
                if (write && !logout && !identity.CanWrite)
                    throw new FleetException(403, "read_only", "An operator key is required to change fleet resources.");
            }
            return await next(context);
        }
        catch (FleetException ex) { return Results.Json(new { code = ex.Code, message = ex.Message }, statusCode: ex.Status); }
        catch (DomainException ex) { return Results.Json(new { code = ex.Code, message = ex.Message }, statusCode: 409); }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        { return Results.Json(new { code = "invalid_request", message = "The request is invalid." }, statusCode: 400); }
    }
}
