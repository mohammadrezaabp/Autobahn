namespace Autobahn.Architecture.Tests;

public sealed class SolutionLayoutTests
{
    private static readonly string[] ExpectedSrcProjects =
    [
        "Autobahn.Host",
        "Autobahn.Domain",
        "Autobahn.Infrastructure",
        "Autobahn.Agent",
        "Autobahn.Api",
        "Autobahn.Billing",
        "Autobahn.Application",
        "Autobahn.Observability",
        "Autobahn.OperatorConsole",
        "Autobahn.Persistence",
        "Autobahn.Policy",
        "Autobahn.Proxy",
        "Autobahn.Registry",
        "Autobahn.Security",
    ];

    private static readonly string[] ExpectedTestProjects =
    [
        "Autobahn.Architecture.Tests",
        "Autobahn.Billing.Tests",
        "Autobahn.Conformance.Tests",
        "Autobahn.Application.Tests",
        "Autobahn.Integration.Tests",
        "Autobahn.Observability.Tests",
        "Autobahn.OperatorConsole.Tests",
        "Autobahn.Persistence.Tests",
        "Autobahn.Policy.Tests",
        "Autobahn.Proxy.Tests",
        "Autobahn.Registry.Tests",
        "Autobahn.Security.Tests",
    ];

    [Fact]
    public void Solution_ContainsExpectedProjects()
    {
        var repoRoot = FindRepoRoot();
        var srcDir = Path.Combine(repoRoot, "src");
        var testsDir = Path.Combine(repoRoot, "tests");

        foreach (var name in ExpectedSrcProjects)
        {
            Assert.True(Directory.Exists(Path.Combine(srcDir, name)), $"Missing src project folder: {name}");
            Assert.True(File.Exists(Path.Combine(srcDir, name, $"{name}.csproj")), $"Missing csproj: {name}");
        }

        foreach (var name in ExpectedTestProjects)
        {
            Assert.True(Directory.Exists(Path.Combine(testsDir, name)), $"Missing test project folder: {name}");
            Assert.True(File.Exists(Path.Combine(testsDir, name, $"{name}.csproj")), $"Missing csproj: {name}");
        }

        Assert.True(File.Exists(Path.Combine(repoRoot, "Autobahn.sln")), "Missing Autobahn.sln at repository root.");
    }

    [Fact]
    public void AllProjects_TargetNet10()
    {
        var repoRoot = FindRepoRoot();
        var buildProps = File.ReadAllText(Path.Combine(repoRoot, "Directory.Build.props"));
        Assert.Contains("<TargetFramework>net10.0</TargetFramework>", buildProps, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Autobahn.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root containing Autobahn.sln.");
    }
}
