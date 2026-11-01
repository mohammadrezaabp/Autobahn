using Autobahn.Application.Abstractions;

namespace Autobahn.Security.Hosting;

public sealed class GatewayAuthenticationState : IGatewayAuthenticationState
{
    public bool IsAuthenticationRequired { get; set; }
}
