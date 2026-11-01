using Autobahn.Registry.Services;

namespace Autobahn.Registry.Tests;

public sealed class ProjectWiringTests
{
    [Fact]
    public void Assembly_Loads()
    {
        typeof(ModelRegistryService).Assembly.GetName().Name.Should().Be("Autobahn.Registry");
    }
}