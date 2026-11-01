namespace Autobahn.Application.Abstractions;
/// <summary>
/// Where a page of the trail stopped: a timestamp, plus how many records carrying exactly that
/// timestamp have already been returned.
/// </summary>
/// <remarks>
/// The count is what makes paging safe. The trail is ordered by append, not by a unique key, and
/// nothing stops two records sharing a timestamp — a coarse platform clock, a fixed
/// <see cref = "TimeProvider"/> in a test, or two replicas appending to one file all produce them. A
/// cursor that is only a timestamp has to choose between dropping every record in the boundary
/// group and returning it twice; carrying the count resumes exactly where the page ended.
/// </remarks>
/// <param name = "TimestampUtc">The last returned record's timestamp.</param>
/// <param name = "Skip">How many records with that timestamp were already returned.</param>
public sealed record AuditLogCursor(DateTimeOffset TimestampUtc, int Skip)
{
    /// <summary>Round-trips through <see cref = "TryParse"/>; opaque to clients.</summary>
    public override string ToString() => TimestampUtc.ToUniversalTime().ToString("o", System.Globalization.CultureInfo.InvariantCulture) + (Skip > 0 ? "|" + Skip.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty);
    /// <summary>A plain timestamp is accepted too, so a hand-written query still works.</summary>
    public static bool TryParse(string? value, out AuditLogCursor? cursor)
    {
        cursor = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var bar = value.IndexOf('|', StringComparison.Ordinal);
        var stamp = bar < 0 ? value : value[..bar];
        var skip = 0;
        if (bar >= 0 && !int.TryParse(value[(bar + 1)..], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out skip))
        {
            return false;
        }

        if (skip < 0 || !DateTimeOffset.TryParse(stamp, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
        {
            return false;
        }

        cursor = new AuditLogCursor(parsed.ToUniversalTime(), skip);
        return true;
    }
}
