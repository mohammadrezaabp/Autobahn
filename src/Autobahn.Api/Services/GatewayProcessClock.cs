namespace Autobahn.Api.Services;

public sealed class GatewayProcessClock
{
    public DateTimeOffset StartedUtc { get; } = DateTimeOffset.UtcNow;
}
