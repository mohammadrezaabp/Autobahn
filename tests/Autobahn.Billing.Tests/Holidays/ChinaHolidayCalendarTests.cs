using Microsoft.Extensions.Configuration;
using Autobahn.Billing.Holidays;
using Autobahn.Domain.Billing;
using Autobahn.Domain.Configuration;

namespace Autobahn.Billing.Tests.Holidays;

/// <summary>
/// China's days off come from the lunar-csharp package. The expected 2026 dates are the State
/// Council's arrangement as NateScarlet/holiday-cn publishes it, an independent copy of the notice.
/// </summary>
public sealed class ChinaHolidayCalendarTests
{
    private static readonly string[] DaysOff2026 =
    [
        "2026-01-01", "2026-01-02", "2026-01-03",
        "2026-02-15", "2026-02-16", "2026-02-17", "2026-02-18", "2026-02-19", "2026-02-20", "2026-02-21", "2026-02-22", "2026-02-23",
        "2026-04-04", "2026-04-05", "2026-04-06",
        "2026-05-01", "2026-05-02", "2026-05-03", "2026-05-04", "2026-05-05",
        "2026-06-19", "2026-06-20", "2026-06-21",
        "2026-09-25", "2026-09-26", "2026-09-27",
        "2026-10-01", "2026-10-02", "2026-10-03", "2026-10-04", "2026-10-05", "2026-10-06", "2026-10-07",
    ];

    [Fact]
    public void DaysOff2026_AreTheStateCouncilsArrangement()
    {
        new ChinaHolidayCalendar().DaysOff(2026)
            .Should().BeEquivalentTo(DaysOff2026.Select(d => DateOnly.Parse(d, System.Globalization.CultureInfo.InvariantCulture)));
    }

    /// <summary>A weekend day worked to make up for a holiday is not a day off.</summary>
    [Theory]
    [InlineData("2026-01-04")]
    [InlineData("2026-02-14")]
    [InlineData("2026-02-28")]
    [InlineData("2026-05-09")]
    [InlineData("2026-09-20")]
    [InlineData("2026-10-10")]
    public void AMakeUpWorkingDay_IsNotADayOff(string date)
    {
        new ChinaHolidayCalendar().DaysOff(2026)
            .Should().NotContain(DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void AYearWithNoArrangementYet_HasNoDaysOff()
    {
        new ChinaHolidayCalendar().DaysOff(2100).Should().BeEmpty();
    }

    /// <summary>A configured provider schedule binds and applies its holiday rules.</summary>
    [Fact]
    public void ConfiguredSchedule_PricesPeakHoursAndExcludesHolidays()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(RepoRoot(), "config", "billing.example.json"), optional: false)
            .Build();
        var options = configuration.GetSection(BillingOptions.SectionName).Get<BillingOptions>()!;

        BillingOptionsValidation.Validate(options).Should().BeEmpty();
        var policy = new PeakPricingPolicy(options.PeakPricing, [new ChinaHolidayCalendar()]);
        policy.MultiplierAt("deepseek", At("2026-10-20T02:00")).Should().Be(2m);
        policy.MultiplierAt("deepseek", At("2026-10-20T12:00")).Should().Be(1m);
        policy.MultiplierAt("deepseek", At("2026-10-01T02:00")).Should().Be(1m, "1 October is National Day");
        policy.MultiplierAt("deepseek", At("2026-02-16T07:00")).Should().Be(1m, "16 February is in the Spring Festival break");
        policy.MultiplierAt("deepseek", At("2026-08-14T02:00")).Should().Be(1m, "peak pricing began on 16 August 2026");
    }

    private static DateTimeOffset At(string utc) =>
        DateTimeOffset.Parse(utc + "Z", System.Globalization.CultureInfo.InvariantCulture);

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Autobahn.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Autobahn.sln not found above the test output.");
    }
}
