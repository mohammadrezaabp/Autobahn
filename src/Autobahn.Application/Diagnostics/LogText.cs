namespace Autobahn.Application.Diagnostics;

/// <summary>Bounds untrusted log fields and keeps each field on one physical line.</summary>
public static class LogText
{
    public static string OneLine(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var bounded = value.Length > 1024 ? value[..1024] : value;
        return bounded.Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\u0085", "\\u0085", StringComparison.Ordinal)
            .Replace("\u2028", "\\u2028", StringComparison.Ordinal)
            .Replace("\u2029", "\\u2029", StringComparison.Ordinal);
    }

    /// <summary>Omits credentials, query parameters and fragments from upstream diagnostics.</summary>
    public static string Endpoint(Uri? value) => value is null ? string.Empty :
        OneLine(value.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped));
}
