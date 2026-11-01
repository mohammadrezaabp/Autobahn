namespace Autobahn.Application.Abstractions;

public interface IGatewayAuthenticationState
{
    bool IsAuthenticationRequired { get; }
}
