using Autobahn.Domain.Validation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Autobahn.Api.Services;
using Autobahn.Application.Providers;
using Autobahn.Application.Security;

namespace Autobahn.Api.Endpoints;
public sealed record ProviderModelsDiscoveryRequest(string? EnvVar);
