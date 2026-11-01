namespace Autobahn.OperatorConsole.Tests;

public sealed class ProjectWiringTests
{
    [Fact]
    public void Assembly_Loads()
    {
        typeof(Autobahn.OperatorConsole.Commands.ConsoleCommandParser).Assembly.GetName().Name.Should().Be("Autobahn.OperatorConsole");
    }
}
