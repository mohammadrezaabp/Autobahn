using System.Reflection;
using FluentAssertions;

namespace Autobahn.Application.Tests;

public sealed class CoreAssemblyTests
{
    [Fact]
    public void Core_HasNoNuGetPackageReferencesBeyondSdk()
    {
        var repoRoot = FindRepoRoot();
        var projectPath = Path.Combine(repoRoot, "src", "Autobahn.Application", "Autobahn.Application.csproj");

        var content = File.ReadAllText(projectPath);

        content.Should().NotContain("PackageReference", "Autobahn.Application must not reference NuGet packages beyond the BCL/SDK");
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
