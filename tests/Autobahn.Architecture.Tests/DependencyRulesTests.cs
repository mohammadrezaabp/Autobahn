using System.Reflection;
using NetArchTest.Rules;

namespace Autobahn.Architecture.Tests;

public sealed class DependencyRulesTests
{
    private static readonly Assembly CoreAssembly = typeof(Autobahn.Domain.Configuration.GatewayOptions).Assembly;
    private static readonly Assembly RegistryAssembly = typeof(Autobahn.Registry.Services.ModelRegistryService).Assembly;
    private static readonly Assembly ProxyAssembly = typeof(Autobahn.Proxy.Middleware.ModelRouterMiddleware).Assembly;
    private static readonly Assembly ApiAssembly = typeof(Autobahn.Api.Endpoints.ConfigAdminEndpoints).Assembly;
    private static readonly Assembly OperatorConsoleAssembly = typeof(Autobahn.OperatorConsole.Commands.ConsoleCommandParser).Assembly;
    private static readonly Assembly PersistenceAssembly = typeof(Autobahn.Persistence.GatewayDbContext).Assembly;
    private static readonly Assembly ObservabilityAssembly = typeof(Autobahn.Observability.Runtime.GatewayRuntimeState).Assembly;
    private static readonly Assembly BillingAssembly = typeof(Autobahn.Billing.DependencyInjection.BillingServiceCollectionExtensions).Assembly;

    private static readonly Assembly[] FeatureAssemblies =
    [
        RegistryAssembly,
        ProxyAssembly,
        ApiAssembly,
        typeof(Autobahn.Policy.RateLimiting.RateLimitPolicyResolver).Assembly,
        ObservabilityAssembly,
        BillingAssembly,
        PersistenceAssembly,
        typeof(Autobahn.Security.DependencyInjection.SecurityServiceCollectionExtensions).Assembly,
        OperatorConsoleAssembly,
    ];

    [Fact]
    public void Core_ShouldNotReferenceAspNetEfOrYarp()
    {
        var result = Types.InAssembly(CoreAssembly)
            .Should()
            .NotHaveDependencyOnAny(
                "Microsoft.AspNetCore",
                "Microsoft.EntityFrameworkCore",
                "Yarp")
            .GetResult();

        AssertArchitectureRule(result);
    }

    [Fact]
    public void Registry_ShouldNotReferenceHttpPipelineTypes()
    {
        var result = Types.InAssembly(RegistryAssembly)
            .Should()
            .NotHaveDependencyOnAny(
                "Microsoft.AspNetCore",
                "Microsoft.AspNetCore.Http",
                "Yarp",
                "Yarp.ReverseProxy")
            .GetResult();

        AssertArchitectureRule(result);
    }

    [Fact]
    public void Proxy_ShouldNotReferencePersistence()
    {
        var result = Types.InAssembly(ProxyAssembly)
            .Should()
            .NotHaveDependencyOn("Autobahn.Persistence")
            .GetResult();

        AssertArchitectureRule(result);
    }

    [Fact]
    public void Api_ShouldOnlyReferenceCoreAmongFeatureLibraries()
    {
        var result = Types.InAssembly(ApiAssembly)
            .Should()
            .NotHaveDependencyOnAny(
                "Autobahn.Proxy",
                "Autobahn.Security",
                "Autobahn.Policy",
                "Autobahn.Registry",
                "Autobahn.Persistence",
                "Autobahn.Billing",
                "Autobahn.Observability",
                "Autobahn.OperatorConsole",
                "Autobahn.Host")
            .GetResult();

        AssertArchitectureRule(result);
    }

    [Fact]
    public void OperatorConsole_ShouldNotReferenceAspNetYarpProxyOrApi()
    {
        var result = Types.InAssembly(OperatorConsoleAssembly)
            .Should()
            .NotHaveDependencyOnAny(
                "Microsoft.AspNetCore",
                "Yarp",
                "Yarp.ReverseProxy",
                "Autobahn.Proxy",
                "Autobahn.Api")
            .GetResult();

        AssertArchitectureRule(result);
    }

    [Fact]
    public void Policy_ShouldNotReferenceProxyOrRegistry()
    {
        var result = Types.InAssembly(typeof(Autobahn.Policy.RateLimiting.RateLimitPolicyResolver).Assembly)
            .Should()
            .NotHaveDependencyOnAny("Autobahn.Proxy", "Autobahn.Registry")
            .GetResult();

        AssertArchitectureRule(result);
    }

    [Fact]
    public void Persistence_ShouldOnlyReferenceCore()
    {
        var result = Types.InAssembly(PersistenceAssembly)
            .Should()
            .NotHaveDependencyOnAny(
                "Autobahn.Proxy",
                "Autobahn.Security",
                "Autobahn.Policy",
                "Autobahn.Registry",
                "Autobahn.Billing",
                "Autobahn.Observability",
                "Autobahn.Api",
                "Autobahn.OperatorConsole",
                "Autobahn.Host")
            .GetResult();

        AssertArchitectureRule(result);
    }

    [Fact]
    public void Observability_ShouldOnlyReferenceCore()
    {
        var result = Types.InAssembly(ObservabilityAssembly)
            .Should()
            .NotHaveDependencyOnAny(
                "Autobahn.Proxy",
                "Autobahn.Security",
                "Autobahn.Policy",
                "Autobahn.Registry",
                "Autobahn.Persistence",
                "Autobahn.Billing",
                "Autobahn.Api",
                "Autobahn.OperatorConsole",
                "Autobahn.Host")
            .GetResult();

        AssertArchitectureRule(result);
    }

    [Fact]
    public void FeatureAssemblies_ShouldNotHaveCircularReferences()
    {
        var names = FeatureAssemblies.Select(a => a.GetName().Name!).ToHashSet(StringComparer.Ordinal);
        var graph = FeatureAssemblies.ToDictionary(
            a => a.GetName().Name!,
            a => a.GetReferencedAssemblies()
                .Select(r => r.Name!)
                .Where(name => names.Contains(name))
                .ToArray());

        foreach (var start in graph.Keys)
        {
            AssertNoCycleFrom(start, graph, []);
        }
    }

    private static void AssertNoCycleFrom(
        string node,
        IReadOnlyDictionary<string, string[]> graph,
        HashSet<string> visited)
    {
        if (!visited.Add(node))
        {
            Assert.Fail($"Circular project reference detected involving '{node}'.");
        }

        if (graph.TryGetValue(node, out var edges))
        {
            foreach (var next in edges)
            {
                AssertNoCycleFrom(next, graph, visited);
            }
        }

        visited.Remove(node);
    }

    private static void AssertArchitectureRule(TestResult result)
    {
        var failures = result.FailingTypes ?? [];
        failures.Should().BeEmpty($"architecture rule failed: {string.Join(", ", failures)}");
    }
}
