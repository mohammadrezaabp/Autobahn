namespace Autobahn.Domain.Models;
/// <summary>
/// Capture points that produce error records. Part of the fingerprint, so the same failure seen by
/// two of them groups as two rows rather than one double-counted total.
/// </summary>
public static class GatewayErrorSourceNames
{
    /// <summary>The inference proxy, which knows the model, upstream and outcome.</summary>
    public const string Proxy = "proxy";
    /// <summary>The terminal exception handler, covering admin and non-inference routes.</summary>
    public const string Exception = "exception";
    /// <summary>An <c>ILogger</c> call at Error or above, mirrored by the admin log sink.</summary>
    public const string Log = "log";
    /// <summary>The admin model-test probe.</summary>
    public const string ModelTest = "modeltest";
    /// <summary>Background health: a backend probe transitioning to unhealthy, or a credential that no longer decrypts.</summary>
    public const string Health = "health";
}
