using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Autobahn.Application.Abstractions;
using Autobahn.Domain.Identity;
using Autobahn.Application.Security;
using Autobahn.Security.Authentication;
using Autobahn.Security.Configuration;

namespace Autobahn.Security.Authorization;
public sealed class GatewayAuthorizationRequirement(string policyName) : IAuthorizationRequirement
{
    public string PolicyName { get; } = policyName;
}
