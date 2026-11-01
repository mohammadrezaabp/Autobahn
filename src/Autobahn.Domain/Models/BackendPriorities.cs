using System.Text.Json.Serialization;

namespace Autobahn.Domain.Models;
/// <summary>When a backend takes traffic: with the others (<c>primary</c>), or only when they cannot (<c>standby</c>).</summary>
public static class BackendPriorities
{
    public const string Primary = "primary";
    public const string Standby = "standby";
    public static bool IsStandby(string? priority) => string.Equals(priority?.Trim(), Standby, StringComparison.OrdinalIgnoreCase);
    /// <summary>Blank is primary, which is what every backend was before the field existed.</summary>
    public static string Normalize(string? priority) => IsStandby(priority) ? Standby : Primary;
    public static bool TryNormalize(string? priority, out string normalized, out string? error)
    {
        error = null;
        normalized = Primary;
        if (string.IsNullOrWhiteSpace(priority) || string.Equals(priority.Trim(), Primary, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (IsStandby(priority))
        {
            normalized = Standby;
            return true;
        }

        error = $"priority '{priority.Trim()}' is not recognised. Expected '{Primary}' or '{Standby}'.";
        return false;
    }
}
