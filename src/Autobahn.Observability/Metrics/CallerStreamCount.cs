using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Autobahn.Application.Observability;

namespace Autobahn.Observability.Metrics;
public sealed class CallerStreamCount
{
    private long _open;
    private long _peak;
    public long Open => Interlocked.Read(ref _open);
    /// <summary>The most streams open at once since the process started.</summary>
    public long Peak => Interlocked.Read(ref _peak);

    public void StreamStarted()
    {
        var open = Interlocked.Increment(ref _open);
        var peak = Interlocked.Read(ref _peak);
        while (open > peak)
        {
            var seen = Interlocked.CompareExchange(ref _peak, open, peak);
            if (seen == peak)
            {
                break;
            }

            peak = seen;
        }
    }

    public void StreamEnded() => Interlocked.Decrement(ref _open);
}
