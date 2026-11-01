using FluentAssertions;
using Autobahn.Domain.Models;
using Autobahn.Proxy.Resilience;

namespace Autobahn.Proxy.Tests.Resilience;

public sealed class BackendInFlightTrackerTests
{
    [Fact]
    public void Enter_CountsUntilTheLeaseIsDisposed_PerBackend()
    {
        var tracker = new BackendInFlightTracker();

        var first = tracker.Enter("m1", "a");
        var second = tracker.Enter("m1", "a");
        using var other = tracker.Enter("m1", "b");

        tracker.GetInFlight("m1", "a").Should().Be(2);
        tracker.GetInFlight("m1", "b").Should().Be(1);
        tracker.GetInFlight("m1", ModelBackends.DefaultName).Should().Be(0);

        first.Dispose();
        first.Dispose();
        tracker.GetInFlight("m1", "a").Should().Be(1, "disposing a lease twice releases it once");

        second.Dispose();
        tracker.GetInFlight("m1", "a").Should().Be(0);
    }
}
