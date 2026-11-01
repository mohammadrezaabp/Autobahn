using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Autobahn.Application.Abstractions;
using Autobahn.Domain.Billing;
using Autobahn.Domain.Models;
using Autobahn.Application.Security;

namespace Autobahn.Api.Endpoints;
/// <summary>The body of a reprice. <c>dryRun</c> defaults to true: writing takes an explicit false.</summary>
public sealed record RepriceBody(string? ModelId, DateOnly? From, DateOnly? To, bool? DryRun);
