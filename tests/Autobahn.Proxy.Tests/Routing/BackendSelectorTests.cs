using FluentAssertions;
using Autobahn.Domain.Models;
using Autobahn.Proxy.Routing;

namespace Autobahn.Proxy.Tests.Routing;

public sealed class BackendSelectorTests
{
    [Fact]
    public void Order_NoBackends_ReturnsNothing()
    {
        BackendSelector.Order([], estimatedTokens: 10).Should().BeEmpty();
    }

    [Fact]
    public void Order_SmallRequest_PrefersTheSmallestContextThatHoldsIt()
    {
        // The long-context backend is idle and the ordinary one is busy: load alone would pick the
        // long one, which is exactly what size-first is there to prevent.
        var ordered = BackendSelector.Order(
            [Candidate("long", 1_000_000, inFlight: 0), Candidate("ordinary", 262_144, inFlight: 40)],
            estimatedTokens: 2_000);

        ordered.Select(b => b.Name).Should().Equal("ordinary", "long");
    }

    [Fact]
    public void Order_RequestLongerThanTheSmallBackend_LeavesItOut()
    {
        var ordered = BackendSelector.Order(
            [Candidate("ordinary", 262_144, inFlight: 0), Candidate("long", 1_000_000, inFlight: 9)],
            estimatedTokens: 300_000);

        ordered.Select(b => b.Name).Should().Equal("long");
    }

    [Fact]
    public void Order_SameSize_PrefersFewestInFlight()
    {
        var ordered = BackendSelector.Order(
            [Candidate("a", 8192, inFlight: 5), Candidate("b", 8192, inFlight: 1), Candidate("c", 8192, inFlight: 3)],
            estimatedTokens: 100);

        ordered.Select(b => b.Name).Should().Equal("b", "c", "a");
    }

    [Fact]
    public void Order_SameSizeAndLoad_SharesTheTrafficBetweenThem()
    {
        var firsts = new HashSet<string>();
        var random = new Random(7);
        for (var i = 0; i < 200; i++)
        {
            firsts.Add(BackendSelector.Order(
                [Candidate("a", 8192, inFlight: 0), Candidate("b", 8192, inFlight: 0)],
                estimatedTokens: 100,
                random)[0].Name);
        }

        firsts.Should().BeEquivalentTo(["a", "b"]);
    }

    [Fact]
    public void Order_NothingLargeEnough_OffersTheLargestRatherThanRefusing()
    {
        var ordered = BackendSelector.Order(
            [Candidate("small", 8192, inFlight: 0), Candidate("large", 32_768, inFlight: 7)],
            estimatedTokens: 500_000);

        ordered.Select(b => b.Name).Should().Equal("large", "small");
    }

    [Fact]
    public void Order_BackendWithNoKnownLimit_HoldsAnythingAndRanksLargest()
    {
        var ordered = BackendSelector.Order(
            [Candidate("unlimited", 0, inFlight: 0), Candidate("small", 8192, inFlight: 3)],
            estimatedTokens: 100);
        ordered.Select(b => b.Name).Should().Equal("small", "unlimited");

        BackendSelector.Order(
                [Candidate("unlimited", 0, inFlight: 0), Candidate("small", 8192, inFlight: 3)],
                estimatedTokens: 100_000)
            .Select(b => b.Name).Should().Equal("unlimited");
    }

    private static BackendCandidate Candidate(string name, int maxContextLength, int inFlight) =>
        new(new ModelBackendConfig { Name = name, Url = $"http://{name}", MaxContextLength = maxContextLength }, inFlight);
}
