using System.Globalization;

namespace LT.ODM.Application.StyleLibrary;

/// <summary>
/// The "generated" time printed on downloads (tech pack, line sheet), in the reader's time zone. The browser sends its
/// IANA zone (e.g. Asia/Ho_Chi_Minh) as ?tz=; without one (or with an unknown one) the server's own zone is used.
/// The offset is always printed, so the time is never ambiguous: "2026-11-05 15:29 (UTC+07:00)".
/// </summary>
public static class GeneratedStamp
{
    /// <summary>The time zone for an IANA or Windows id; null when blank or unknown.</summary>
    public static TimeZoneInfo? Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64) return null;
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id.Trim());
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return null;
        }
    }

    public static string Format(DateTime generatedUtc, TimeZoneInfo? timeZone)
    {
        var zone = timeZone ?? TimeZoneInfo.Local;
        var utc = DateTime.SpecifyKind(generatedUtc, DateTimeKind.Utc);
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, zone);
        var offset = zone.GetUtcOffset(utc);
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        return local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + $" (UTC{sign}{offset:hh\\:mm})";
    }
}
