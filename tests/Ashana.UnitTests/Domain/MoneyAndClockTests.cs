using Ashana.Application.Common;
using Ashana.UnitTests.Support;
using Microsoft.Extensions.Options;

namespace Ashana.UnitTests.Domain;

public class MoneyTests
{
    [Theory]
    [InlineData(0, "0 ₸")]
    [InlineData(950, "950 ₸")]
    [InlineData(1800, "1 800 ₸")]
    [InlineData(1234567, "1 234 567 ₸")]
    public void Whole_tenge_use_thin_group_spaces_that_never_wrap(int amount, string expected) =>
        Assert.Equal(expected, Money.Tenge(amount));

    [Fact]
    public void Fractions_keep_two_decimals() =>
        Assert.Equal("1 800,50 ₸", Money.Tenge(1800.5m));
}

public class CanteenClockTests
{
    [Fact]
    public void Unknown_time_zone_falls_back_to_the_configured_offset()
    {
        var clock = new CanteenClock(new ManualTimeProvider(TestData.Now), Options.Create(new CanteenOptions { TimeZone = "Nowhere/Unknown", FallbackUtcOffsetHours = 5 }));

        Assert.Equal(TimeSpan.FromHours(5), clock.TimeZone.BaseUtcOffset);
        Assert.Equal(TestData.Now, clock.Now);
        Assert.Equal(TimeSpan.FromHours(5), clock.Now.Offset);
        Assert.Equal(TestData.Today, clock.Today);
    }

    [Theory]
    [InlineData(5, "UTC+05:00")]
    [InlineData(-3, "UTC-03:00")]
    public void Fallback_zone_is_named_after_its_offset(int hours, string expectedId)
    {
        var clock = new CanteenClock(new ManualTimeProvider(TestData.Now), Options.Create(new CanteenOptions { TimeZone = "Nowhere/Unknown", FallbackUtcOffsetHours = hours }));

        Assert.Equal(expectedId, clock.TimeZone.Id);
        Assert.Equal(TimeSpan.FromHours(hours), clock.Now.Offset);
    }

    [Fact]
    public void Server_time_in_utc_is_shown_in_canteen_time()
    {
        var utcLateEvening = new DateTimeOffset(2026, 10, 30, 21, 30, 0, TimeSpan.Zero); // 02:30 next day in the canteen
        var clock = new CanteenClock(new ManualTimeProvider(utcLateEvening), Options.Create(new CanteenOptions { TimeZone = "Nowhere/Unknown", FallbackUtcOffsetHours = 5 }));

        Assert.Equal(new DateOnly(2026, 10, 31), clock.Today);
        Assert.Equal(new TimeOnly(2, 30), TimeOnly.FromDateTime(clock.Now.DateTime));
    }
}
