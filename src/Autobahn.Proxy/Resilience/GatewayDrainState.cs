using Autobahn.Application.Abstractions;

namespace Autobahn.Proxy.Resilience;

public sealed class GatewayDrainState : IGatewayDrainState
{
    private int _isDraining;

    public bool IsDraining => Volatile.Read(ref _isDraining) != 0;

    public void BeginDrain() => Volatile.Write(ref _isDraining, 1);
}
