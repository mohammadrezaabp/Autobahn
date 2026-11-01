using Autobahn.Domain.Models;

namespace Autobahn.Domain.Identity;

/// <summary>
/// The three forms a grant's <c>ModelPattern</c> takes: an exact model id, a prefix ending in
/// <c>*</c> (<c>qwen/*</c>), and every model of one provider (<c>openrouter:*</c>), which also
/// covers models registered later.
/// </summary>
public static class ModelGrantPattern
{
    public const int MaxLength = 256;

    private const char Wildcard = '*';

    public static bool IsWildcard(string pattern) => pattern.Length > 0 && pattern[^1] == Wildcard;

    /// <summary>The pattern for every model of <paramref name="provider"/>.</summary>
    public static string ForProvider(string provider) => provider + ":" + Wildcard;

    /// <summary>The provider a <c>provider:*</c> pattern names, or null for any other pattern.</summary>
    public static string? ProviderOf(string pattern)
    {
        if (!IsWildcard(pattern) || pattern.Length < 3 || pattern[^2] != ':')
        {
            return null;
        }

        var name = pattern[..^2];
        return ModelProviders.All.FirstOrDefault(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Checks a wildcard pattern as an operator typed it. Only a trailing <c>*</c> is accepted, and
    /// it needs something in front of it: a bare <c>*</c> would grant every model there will ever be.
    /// </summary>
    public static bool TryValidateWildcard(string? pattern, out string? error)
    {
        error = null;
        var trimmed = pattern?.Trim() ?? string.Empty;

        if (trimmed.Length > MaxLength)
        {
            error = $"pattern '{trimmed[..32]}…' is longer than {MaxLength} characters.";
            return false;
        }

        if (!IsWildcard(trimmed) || trimmed.IndexOf(Wildcard) != trimmed.Length - 1)
        {
            error = $"pattern '{trimmed}' must end in '*' and contain no other '*'.";
            return false;
        }

        if (trimmed.Length == 1)
        {
            error = "pattern '*' is not accepted: name a prefix ('qwen/*') or a provider ('openrouter:*').";
            return false;
        }

        return true;
    }

    public static bool Matches(string pattern, string modelId, string provider)
    {
        if (!IsWildcard(pattern))
        {
            return string.Equals(pattern, modelId, StringComparison.OrdinalIgnoreCase);
        }

        // A bare '*' is never written by the admin API; a row holding one grants nothing.
        if (pattern.Length == 1)
        {
            return false;
        }

        var named = ProviderOf(pattern);
        if (named is not null)
        {
            return string.Equals(named, provider, StringComparison.OrdinalIgnoreCase);
        }

        return modelId.AsSpan().StartsWith(pattern.AsSpan(0, pattern.Length - 1), StringComparison.OrdinalIgnoreCase);
    }
}
