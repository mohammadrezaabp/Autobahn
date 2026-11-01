using NSubstitute;
using Autobahn.Api.Services;
using Autobahn.Application.Abstractions;
using Autobahn.Domain.Models;

namespace Autobahn.Api.Tests.Services;

public sealed class GatewayStatsServiceTests
{
    [Fact]
    public void GetSnapshot_MapsFromAdminSummary()
    {
        var summaryReader = Substitute.For<IAdminSummaryReader>();
        summaryReader.GetSnapshot().Returns(new AdminSummarySnapshot
        {
            Uptime = "00.00:00:05",
            UptimeSeconds = 5,
            TotalInferenceRequests = 10,
            TotalErrors = 2,
            AverageLatencyMs = 12.5,
            ActiveStreams = 1,
            RateLimitRejections = 3,
            QuotaRejections = 1,
            RequestsPerModel = new Dictionary<string, long> { ["m1"] = 10 },
            ErrorsPerModel = new Dictionary<string, long> { ["m1"] = 2 },
        });

        var service = new GatewayStatsService(summaryReader);
        var snapshot = service.GetSnapshot();

        snapshot.TotalRequests.Should().Be(10);
        snapshot.Uptime.Should().Be("00.00:00:05");
        snapshot.RequestsPerModel.Should().ContainKey("m1");
        snapshot.ActiveConnections.Should().Be(1);
    }
}
