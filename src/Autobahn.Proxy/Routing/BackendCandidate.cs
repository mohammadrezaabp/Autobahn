using Autobahn.Domain.Models;

namespace Autobahn.Proxy.Routing;
/// <param name = "Backend">A backend that is in service and healthy.</param>
/// <param name = "InFlight">Requests currently being forwarded to it.</param>
public readonly record struct BackendCandidate(ModelBackendConfig Backend, int InFlight);
