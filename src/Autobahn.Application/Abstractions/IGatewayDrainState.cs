namespace Autobahn.Application.Abstractions;

public interface IGatewayDrainState
{
    bool IsDraining { get; }

    void BeginDrain();
}
