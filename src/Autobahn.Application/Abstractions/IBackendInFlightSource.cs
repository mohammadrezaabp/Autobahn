namespace Autobahn.Application.Abstractions;

/// <summary>
/// How many requests this gateway process is forwarding to each backend right now. It is what the
/// router balances on, and what tells the caller of the admin API that a stopped backend has
/// emptied and can be taken down.
/// </summary>
public interface IBackendInFlightSource
{
    int GetInFlight(string modelId, string backendName);
}
