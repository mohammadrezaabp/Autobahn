using System.Reflection;

namespace Autobahn.Application.Diagnostics;

/// <summary>The release the running process was built as.</summary>
public static class GatewayBuildInfo
{
    /// <summary>
    /// The host assembly's informational version without its source-revision suffix, for example
    /// <c>2.2.0-rc.2</c>. <c>0.0.0</c> when the host carries none.
    /// </summary>
    public static string Version { get; } = Resolve(Assembly.GetEntryAssembly());

    internal static string Resolve(Assembly? assembly)
    {
        var informational = assembly?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (string.IsNullOrWhiteSpace(informational))
        {
            return "0.0.0";
        }

        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        return plus > 0 ? informational[..plus] : informational;
    }
}
