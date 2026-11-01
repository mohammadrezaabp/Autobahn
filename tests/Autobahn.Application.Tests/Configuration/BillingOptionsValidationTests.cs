using Autobahn.Domain.Configuration;

namespace Autobahn.Application.Tests.Configuration;

/// <summary>
/// BillingOptions used to be bound without range checks, and the failure modes were silent: a
/// non-positive flush interval killed the usage writer's timer loop unobserved, an out-of-range
/// webhook hour meant the daily summary never fired, and a poll interval above an hour could skip
/// the scheduled hour entirely.
/// </summary>
public sealed class BillingOptionsValidationTests
{
    [Fact]
    public void Validate_Defaults_ReturnNoErrors()
    {
        BillingOptionsValidation.Validate(new BillingOptions()).Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_NonPositiveFlushInterval_ReturnsError(int ms)
    {
        var errors = BillingOptionsValidation.Validate(new BillingOptions { UsageWriterFlushIntervalMs = ms });

        errors.Should().ContainSingle(e => e.Contains(nameof(BillingOptions.UsageWriterFlushIntervalMs), StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_NonPositiveBatchSize_ReturnsError()
    {
        var errors = BillingOptionsValidation.Validate(new BillingOptions { UsageWriterBatchSize = 0 });

        errors.Should().ContainSingle(e => e.Contains(nameof(BillingOptions.UsageWriterBatchSize), StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(24)]
    public void Validate_WebhookHourOutOfRange_ReturnsError(int hour)
    {
        var errors = BillingOptionsValidation.Validate(new BillingOptions { DailyWebhookUtcHour = hour });

        errors.Should().ContainSingle(e => e.Contains(nameof(BillingOptions.DailyWebhookUtcHour), StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3601)]
    public void Validate_WebhookPollIntervalOutOfRange_ReturnsError(int seconds)
    {
        var errors = BillingOptionsValidation.Validate(new BillingOptions { DailyWebhookPollIntervalSeconds = seconds });

        errors.Should().ContainSingle(e => e.Contains(nameof(BillingOptions.DailyWebhookPollIntervalSeconds), StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_NonPositiveCacheTtls_ReturnErrors()
    {
        var errors = BillingOptionsValidation.Validate(new BillingOptions
        {
            RateCardCacheTtlSeconds = 0,
            BudgetCacheTtlSeconds = 0,
            BudgetSpendCacheTtlSeconds = -5,
        });

        errors.Should().HaveCount(3);
        errors.Should().Contain(e => e.Contains(nameof(BillingOptions.RateCardCacheTtlSeconds), StringComparison.Ordinal));
        errors.Should().Contain(e => e.Contains(nameof(BillingOptions.BudgetCacheTtlSeconds), StringComparison.Ordinal));
        errors.Should().Contain(e => e.Contains(nameof(BillingOptions.BudgetSpendCacheTtlSeconds), StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_RetentionAndReconciliationWindows_MustBeAtLeastOneDay()
    {
        var errors = BillingOptionsValidation.Validate(new BillingOptions
        {
            UsageRetentionDays = 0,
            ReconciliationLookbackDays = 0,
            ReconciliationIntervalMinutes = 0,
        });

        errors.Should().HaveCount(3);
    }

    [Fact]
    public void Validate_UsageWriterRetryKnobs_AreRangeChecked()
    {
        var errors = BillingOptionsValidation.Validate(new BillingOptions
        {
            UsageWriterMaxFlushRetries = -1,
            UsageWriterMaxPendingEvents = 0,
        });

        errors.Should().HaveCount(2);
        errors.Should().Contain(e => e.Contains(nameof(BillingOptions.UsageWriterMaxFlushRetries), StringComparison.Ordinal));
        errors.Should().Contain(e => e.Contains(nameof(BillingOptions.UsageWriterMaxPendingEvents), StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_ErrorsNameTheBillingSection()
    {
        var errors = BillingOptionsValidation.Validate(new BillingOptions { DailyWebhookUtcHour = 99 });

        errors.Should().ContainSingle().Which.Should().StartWith(BillingOptions.SectionName + ".");
    }

    [Fact]
    public void Validate_PeakPricingForAPaidProvider_ReturnsNoErrors()
    {
        var errors = BillingOptionsValidation.Validate(WithPeakPricing("deepseek", Schedule()));

        errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("deep-seek")]
    [InlineData("self-hosted")]
    public void Validate_PeakPricingForNoPaidProvider_ReturnsError(string provider)
    {
        // A misspelt id would match no request and leave every peak hour at the off-peak price.
        var errors = BillingOptionsValidation.Validate(WithPeakPricing(provider, Schedule()));

        errors.Should().ContainSingle(e => e.Contains(provider, StringComparison.Ordinal) && e.Contains("no paid provider", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    [InlineData(101)]
    public void Validate_PeakMultiplierOutOfRange_ReturnsError(int multiplier)
    {
        var schedule = Schedule();
        schedule.Multiplier = multiplier;

        var errors = BillingOptionsValidation.Validate(WithPeakPricing("deepseek", schedule));

        errors.Should().Contain(e => e.Contains(nameof(PeakPricingOptions.Multiplier), StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_PeakMultiplierWithNoWindows_ReturnsError()
    {
        var schedule = Schedule();
        schedule.WindowsUtc = [];

        var errors = BillingOptionsValidation.Validate(WithPeakPricing("deepseek", schedule));

        errors.Should().ContainSingle(e => e.Contains("at least one day and one window", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_MalformedPeakWindow_ReturnsError()
    {
        var schedule = Schedule();
        schedule.WindowsUtc = ["01:00-04:00", "22:00-02:00"];

        var errors = BillingOptionsValidation.Validate(WithPeakPricing("deepseek", schedule));

        errors.Should().ContainSingle(e => e.Contains("22:00-02:00", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_AnUnknownHolidayCalendar_ReturnsError()
    {
        var schedule = Schedule();
        schedule.HolidayCalendar = "iran";

        var errors = BillingOptionsValidation.Validate(WithPeakPricing("deepseek", schedule));

        errors.Should().ContainSingle(e => e.Contains("'iran' is not a holiday calendar", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_AMultiplierOfOneWithNoWindows_ReturnsNoErrors()
    {
        // Peak priced as off-peak: nothing to check.
        var errors = BillingOptionsValidation.Validate(WithPeakPricing("deepseek", new PeakPricingOptions { Multiplier = 1m }));

        errors.Should().BeEmpty();
    }

    private static BillingOptions WithPeakPricing(string provider, PeakPricingOptions schedule) =>
        new() { PeakPricing = { [provider] = schedule } };

    private static PeakPricingOptions Schedule() => new()
    {
        Multiplier = 2m,
        Days = [DayOfWeek.Monday, DayOfWeek.Friday],
        WindowsUtc = ["01:00-04:00"],
    };
}
