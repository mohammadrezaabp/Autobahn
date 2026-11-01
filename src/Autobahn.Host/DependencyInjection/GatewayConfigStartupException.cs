using Autobahn.Domain.Configuration;

namespace Autobahn.Host.DependencyInjection;
/// <summary>Raised when the initial configuration load exhausts its startup budget.</summary>
public sealed class GatewayConfigStartupException(string message) : Exception(message);
