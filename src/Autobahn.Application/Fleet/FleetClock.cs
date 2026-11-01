namespace Autobahn.Application.Fleet;

public static class FleetClock
{
    public static double Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d;
}
