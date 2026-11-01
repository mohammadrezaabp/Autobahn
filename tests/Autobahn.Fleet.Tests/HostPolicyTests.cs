using Autobahn.Domain.Common;
using Autobahn.Domain.Fleet;

namespace Autobahn.Fleet.Tests;

public sealed class HostPolicyTests
{
    private static HostPolicy Policy => new() { Enabled = true, Gpus = [0, 1], SharedGpus = [1], Images = ["vllm/vllm-openai"] };
    private static ServerSpecification Server => new() { Name = "chat", Model = "example/chat", Gpus = [0], Port = 8001, Image = "vllm/vllm-openai:v1", ServedName = "chat" };

    [Fact] public void AnAllowedServerPasses() => Policy.EnsureAllowed(Server, []);
    [Fact] public void ADisabledHostRefusesServers() => Assert.Throws<DomainException>(() => (Policy with { Enabled = false }).EnsureAllowed(Server, []));
    [Fact] public void ADisallowedGpuIsRefused() => Assert.Throws<DomainException>(() => Policy.EnsureAllowed(Server with { Gpus = [7] }, []));
    [Fact] public void ARegistryPrefixCannotBypassTheImageAllowlist() => Assert.Throws<DomainException>(() => Policy.EnsureAllowed(Server with { Image = "attacker.test/vllm/vllm-openai:v1" }, []));
    [Fact] public void RegistryPortsArePreservedWhenCheckingImageRepositories() => (Policy with { Images = ["registry.example.com:5000/vllm"] }).EnsureAllowed(Server with { Image = "registry.example.com:5000/vllm:v1" }, []);
    [Fact] public void AnOccupiedPortIsRefused() => Assert.Throws<DomainException>(() => Policy.EnsureAllowed(Server, [Server with { Name = "other", Gpus = [1] }]));
    [Fact] public void AnExclusiveGpuCannotBeShared() => Assert.Throws<DomainException>(() => Policy.EnsureAllowed(Server with { Shared = true }, []));
    [Fact] public void TwoServersMayShareOnlyAnExplicitlySharedGpu() => Policy.EnsureAllowed(Server with { Gpus = [1], Shared = true }, [Server with { Name = "other", Port = 8002, Gpus = [1], Shared = true }]);
    [Fact] public void BothServersMustAgreeToShare() => Assert.Throws<DomainException>(() => Policy.EnsureAllowed(Server with { Gpus = [1], Shared = true }, [Server with { Name = "other", Port = 8002, Gpus = [1] }]));
    [Theory] [InlineData("--api-key=secret")] [InlineData("--port")] [InlineData("--trust-remote-code")]
    public void AgentManagedArgumentsCannotBeOverridden(string argument) => Assert.Throws<DomainException>(() => (Server with { Args = [argument] }).Validate());
    [Theory] [InlineData("../escape")] [InlineData("/root/model")] [InlineData("owner/../../escape")]
    public void ModelPathsCannotEscapeTheStore(string model) => Assert.Throws<DomainException>(() => (Server with { Model = model }).Validate());
}
