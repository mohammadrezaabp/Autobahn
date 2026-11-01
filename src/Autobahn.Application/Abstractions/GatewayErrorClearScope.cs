using Autobahn.Domain.Models;

namespace Autobahn.Application.Abstractions;
public enum GatewayErrorClearScope
{
    /// <summary>Error records, error counters, and failed rows in the Recent requests feed.</summary>
    Errors = 0,
    /// <summary>
    /// Also drops the persisted counter snapshot row outright, resetting total requests, latency
    /// and the whole recent feed with it.
    /// </summary>
    AllCounters = 1,
}
