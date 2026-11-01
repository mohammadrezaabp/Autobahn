using Autobahn.Domain.Common;
using Autobahn.Domain.Fleet;

namespace Autobahn.Fleet.Tests;

public sealed class AggregateTests
{
    [Fact]
    public void AStaleDeploymentCannotBeginApplying()
    {
        var deployment = Deployment.Draft("node", "old", [], ["old-server"]); deployment.Propose();
        Assert.Throws<DomainException>(() => deployment.BeginApply("new"));
        deployment.Status.Should().Be(DeploymentStatus.Proposed);
    }

    [Fact]
    public void ACompletedDeploymentCannotBeAppliedTwice()
    {
        var deployment = Deployment.Draft("node", "one", [], ["old-server"]);
        deployment.BeginApply("one"); deployment.Complete();
        Assert.Throws<DomainException>(() => deployment.BeginApply("one"));
    }

    [Fact] public void AnEmptyDeploymentIsRefused() => Assert.Throws<DomainException>(() => Deployment.Draft("node", "one", [], []));

    [Fact]
    public void DownloadProgressAndTerminalStateAreMonotonic()
    {
        var job = new DownloadJob { Repo = "example/chat" }; job.Start(); job.Progress(100);
        Assert.Throws<DomainException>(() => job.Progress(99));
        job.Finish(DownloadState.Completed);
        Assert.Throws<DomainException>(() => job.Finish(DownloadState.Cancelled));
        Assert.Throws<DomainException>(() => job.Progress(101));
    }
}
