using Autobahn.Domain.Billing;
using Autobahn.Domain.Configuration;

namespace Autobahn.Application.Tests.Billing;

/// <summary>
/// DeepSeek bills twice its off-peak price on weekday mornings in UTC. One price per model was
/// wrong at one of the two times of day whichever price was entered.
/// </summary>
public sealed class PeakPricingPolicyTests
{
    private static readonly PeakPricingPolicy DeepSeek = new(new Dictionary<string, PeakPricingOptions>
    {
        ["deepseek"] = new()
        {
            Multiplier = 2m,
            EffectiveFromUtc = At("2026-08-16T16:00"),
            Days = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday],
            WindowsUtc = ["01:00-04:00", "06:00-10:00"],
            HolidayCalendar = "china",
            ExcludedDates = [new DateOnly(2026, 10, 5)],
        },
    }, [new FakeCalendar("china", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 2))]);

    [Theory]
    [InlineData("2026-10-20T00:59", 1)] // Tuesday, before the first window
    [InlineData("2026-10-20T01:00", 2)] // a window's start is in it
    [InlineData("2026-10-20T03:59", 2)]
    [InlineData("2026-10-20T04:00", 1)] // its end is not
    [InlineData("2026-10-20T05:30", 1)] // between the windows
    [InlineData("2026-10-20T06:00", 2)]
    [InlineData("2026-10-20T09:59", 2)]
    [InlineData("2026-10-20T10:00", 1)]
    [InlineData("2026-10-20T18:00", 1)]
    [InlineData("2026-10-24T02:00", 1)] // Saturday
    [InlineData("2026-10-25T07:00", 1)] // Sunday
    [InlineData("2026-10-05T02:00", 1)] // Monday, a configured day off
    [InlineData("2026-10-01T02:00", 1)] // Thursday, a day off in the calendar
    [InlineData("2026-10-02T07:00", 1)] // Friday, likewise
    [InlineData("2026-10-08T02:00", 2)] // Thursday, back at work
    [InlineData("2026-08-14T02:00", 1)] // Friday, before the provider billed peak hours
    [InlineData("2026-08-17T02:00", 2)] // Monday, after
    public void MultiplierAt_FollowsTheSchedule(string atUtc, int expected)
    {
        DeepSeek.MultiplierAt("deepseek", At(atUtc)).Should().Be(expected);
    }

    [Fact]
    public void MultiplierAt_ReadsTheInstantInUtc()
    {
        // 05:30 in Tehran is 02:00 UTC: peak.
        var tehran = new DateTimeOffset(2026, 10, 20, 5, 30, 0, TimeSpan.FromHours(3.5));

        DeepSeek.MultiplierAt("deepseek", tehran).Should().Be(2m);
    }

    [Theory]
    [InlineData("DeepSeek")]
    [InlineData("DEEPSEEK")]
    public void MultiplierAt_MatchesTheProviderIgnoringCase(string provider)
    {
        DeepSeek.MultiplierAt(provider, At("2026-10-20T02:00")).Should().Be(2m);
    }

    [Theory]
    [InlineData("openrouter")]
    [InlineData("self-hosted")]
    [InlineData("")]
    [InlineData(null)]
    public void MultiplierAt_AnotherProvider_IsFlat(string? provider)
    {
        DeepSeek.MultiplierAt(provider, At("2026-10-20T02:00")).Should().Be(1m);
    }

    [Fact]
    public void Apply_AtPeak_MultipliesTheTokenPricesOnly()
    {
        var card = Card() with { CachedInputPricePerMillionTokens = 0.003m, PricePerRequest = 0.01m };

        var peak = DeepSeek.Apply(card, "deepseek", At("2026-10-20T02:00"));

        peak.InputPricePerMillionTokens.Should().Be(0.30m);
        peak.OutputPricePerMillionTokens.Should().Be(1.20m);
        peak.CachedInputPricePerMillionTokens.Should().Be(0.006m);
        peak.PricePerRequest.Should().Be(0.01m, "a per-request charge has no time of day");
        peak.ModelId.Should().Be(card.ModelId);
    }

    [Fact]
    public void Apply_WithNoCachedPrice_KeepsItUnset()
    {
        var peak = DeepSeek.Apply(Card(), "deepseek", At("2026-10-20T02:00"));

        peak.CachedInputPricePerMillionTokens.Should().BeNull("an unset cached price bills cached tokens as input");
    }

    [Fact]
    public void Apply_OffPeak_ReturnsTheCard()
    {
        var card = Card();

        DeepSeek.Apply(card, "deepseek", At("2026-10-20T12:00")).Should().BeSameAs(card);
    }

    [Fact]
    public void StartOf_TakesTheDurationOffTheRecordedTime()
    {
        // Recorded at 04:01 after two minutes: it ran from 03:59, inside the window.
        var started = PeakPricingPolicy.StartOf(At("2026-10-20T04:01"), durationMs: 120_000);

        started.Should().Be(At("2026-10-20T03:59"));
        DeepSeek.MultiplierAt("deepseek", started).Should().Be(2m);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-5d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void StartOf_WithNoUsableDuration_IsTheRecordedTime(double durationMs)
    {
        PeakPricingPolicy.StartOf(At("2026-10-20T04:01"), durationMs).Should().Be(At("2026-10-20T04:01"));
    }

    [Theory]
    [InlineData("01:00-04:00", 60, 240)]
    [InlineData("00:00-24:00", 0, 1440)]
    [InlineData(" 06:00 - 10:30 ", 360, 630)]
    public void TryParseWindow_ReadsMinutesOfTheDay(string text, int start, int end)
    {
        PeakPricingPolicy.TryParseWindow(text, out var s, out var e).Should().BeTrue();
        s.Should().Be(start);
        e.Should().Be(end);
    }

    [Theory]
    [InlineData("04:00-01:00")] // backwards
    [InlineData("22:00-02:00")] // past midnight: two windows
    [InlineData("01:00-01:00")] // empty
    [InlineData("1:00-4:00")]
    [InlineData("24:00-24:00")]
    [InlineData("01:00")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParseWindow_RejectsWhatIsNotAWindow(string? text)
    {
        PeakPricingPolicy.TryParseWindow(text, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void Constructor_WithAMalformedWindow_Throws()
    {
        var build = () => new PeakPricingPolicy(new Dictionary<string, PeakPricingOptions>
        {
            ["deepseek"] = new() { Multiplier = 2m, Days = [DayOfWeek.Monday], WindowsUtc = ["morning"] },
        });

        build.Should().Throw<ArgumentException>().WithMessage("*morning*");
    }

    [Fact]
    public void Describe_ReturnsTheScheduleOfAProviderThatHasOne()
    {
        var schedule = DeepSeek.Describe("deepseek");

        schedule.Should().NotBeNull();
        schedule!.Multiplier.Should().Be(2m);
        schedule.WindowsUtc.Should().Equal("01:00-04:00", "06:00-10:00");
        schedule.Days.Should().HaveCount(5);
        DeepSeek.Describe("openrouter").Should().BeNull();
    }

    [Fact]
    public void AMultiplierOfOne_IsNoSchedule()
    {
        var policy = new PeakPricingPolicy(new Dictionary<string, PeakPricingOptions>
        {
            ["deepseek"] = new() { Multiplier = 1m, Days = [DayOfWeek.Tuesday], WindowsUtc = ["01:00-04:00"] },
        });

        policy.Describe("deepseek").Should().BeNull();
        policy.MultiplierAt("deepseek", At("2026-10-20T02:00")).Should().Be(1m);
    }

    [Fact]
    public void Constructor_NamingACalendarItIsNotGiven_Throws()
    {
        var build = () => new PeakPricingPolicy(new Dictionary<string, PeakPricingOptions>
        {
            ["deepseek"] = new() { Multiplier = 2m, Days = [DayOfWeek.Monday], WindowsUtc = ["01:00-04:00"], HolidayCalendar = "china" },
        });

        build.Should().Throw<ArgumentException>().WithMessage("*china*");
    }

    [Fact]
    public void UpcomingDaysOff_JoinsTheCalendarsAndTheConfiguredOnes_FromToday()
    {
        DeepSeek.UpcomingDaysOff("deepseek", new DateOnly(2026, 10, 2))
            .Should().Equal(new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 5));
        DeepSeek.UpcomingDaysOff("openrouter", new DateOnly(2026, 10, 2)).Should().BeEmpty();
    }

    /// <summary>A year the calendar has no holidays for prices every weekday with its peak hours, so it is named.</summary>
    [Fact]
    public void YearsWithoutHolidays_NamesTheYearsTheCalendarDoesNotCover()
    {
        DeepSeek.YearsWithoutHolidays("deepseek", new DateOnly(2026, 10, 20)).Should().Equal(2027);
        DeepSeek.YearsWithoutHolidays("openrouter", new DateOnly(2026, 10, 20)).Should().BeEmpty();
    }

    private sealed class FakeCalendar(string name, params DateOnly[] daysOff) : IHolidayCalendar
    {
        public string Name => name;

        public IReadOnlySet<DateOnly> DaysOff(int year) => daysOff.Where(d => d.Year == year).ToHashSet();
    }

    private static RateCardRecord Card() => new(
        Guid.NewGuid(),
        "deepseek-flash",
        "deepseek-flash",
        "deepseek-flash",
        InputPricePerMillionTokens: 0.15m,
        OutputPricePerMillionTokens: 0.60m,
        "USD",
        At("2026-01-01T00:00"),
        EffectiveUntil: null,
        IsActive: true,
        At("2026-01-01T00:00"),
        At("2026-01-01T00:00"));

    private static DateTimeOffset At(string utc) =>
        DateTimeOffset.Parse(utc + "Z", System.Globalization.CultureInfo.InvariantCulture);

}
