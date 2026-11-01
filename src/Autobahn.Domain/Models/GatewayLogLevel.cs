namespace Autobahn.Domain.Models;
/// <summary>
/// The severities the operator-facing log keeps. Deliberately coarser than
/// <c>Microsoft.Extensions.Logging.LogLevel</c>: trace/debug noise never reaches this buffer.
/// </summary>
public enum GatewayLogLevel
{
    Info = 0,
    Warning = 1,
    Error = 2,
    Critical = 3,
}
