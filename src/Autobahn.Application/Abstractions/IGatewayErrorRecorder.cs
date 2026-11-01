using Autobahn.Domain.Models;

namespace Autobahn.Application.Abstractions;
/// <summary>
/// The write half of error tracking, separated so the request path depends only on what it needs.
/// </summary>
/// <remarks>
/// Deliberately synchronous and void-returning: this is called from the middleware that is about
/// to answer a caller. Implementations must never block on I/O and must never throw — failing to
/// record a failure is not worth compounding it.
/// </remarks>
public interface IGatewayErrorRecorder
{
    /// <summary>
    /// Records one occurrence. The implementation computes the fingerprint and applies redaction;
    /// callers pass the raw record.
    /// </summary>
    void Record(GatewayErrorRecord record);
}
