using System.Reflection;

namespace Autobahn.Host;

internal static class GatewayEndpoints
{
    public static IResult GetRoot()
    {
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "0.0.0";

        return Results.Json(new
        {
            name = "Autobahn",
            version,
            documentation = new
            {
                readme = "README.md",
                architecture = "docs/architecture.md",
            },
        });
    }
}
