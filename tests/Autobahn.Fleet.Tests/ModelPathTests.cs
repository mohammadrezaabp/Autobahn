using Autobahn.Domain.Common;
using Autobahn.Infrastructure.Agent;

namespace Autobahn.Fleet.Tests;

public sealed class ModelPathTests
{
    [Theory]
    [InlineData("../escape")][InlineData("/etc/passwd")][InlineData("C:/Windows")][InlineData("a/../../escape")][InlineData("a\\..\\escape")][InlineData(".")][InlineData("")][InlineData("a//b")]
    public void UnsafePathsAreRejected(string relative) => Assert.Throws<DomainException>(() => ModelPaths.Resolve(Path.GetTempPath(), relative));

    [Fact]
    public void ValidPathsStayInsideTheStore()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        ModelPaths.Resolve(root, "example/chat/model.safetensors").Should().StartWith(root + Path.DirectorySeparatorChar);
    }
}
