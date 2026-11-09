using LT.ODM.Application.StyleLibrary;

namespace LT.ODM.Api.Tests;

public sealed class GeneratedStampTests
{
    private static readonly DateTime Utc = new(2026, 11, 5, 7, 29, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("Asia/Ho_Chi_Minh", "2026-11-05 14:29 (UTC+07:00)")]
    [InlineData("Asia/Shanghai", "2026-11-05 15:29 (UTC+08:00)")]
    [InlineData("America/New_York", "2026-11-05 02:29 (UTC-05:00)")]
    [InlineData("UTC", "2026-11-05 07:29 (UTC+00:00)")]
    public void Prints_the_readers_local_time_with_its_offset(string zone, string expected)
        => Assert.Equal(expected, GeneratedStamp.Format(Utc, GeneratedStamp.Find(zone)));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Not/AZone")]
    public void Unknown_or_missing_zones_fall_back_to_the_server_zone(string? zone)
    {
        Assert.Null(GeneratedStamp.Find(zone));
        Assert.Equal(GeneratedStamp.Format(Utc, TimeZoneInfo.Local), GeneratedStamp.Format(Utc, GeneratedStamp.Find(zone)));
    }
}
