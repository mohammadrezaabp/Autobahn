using Autobahn.Application.Providers;

namespace Autobahn.Application.Tests.Providers;

public sealed class ProviderReconcilerTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 14, 0, 0, 0, TimeSpan.Zero);

    private static ProviderBalanceSnapshot Reading(int minutes, decimal remaining, string currency = "USD") =>
        new("deepseek", Start.AddMinutes(minutes), remaining, currency);

    private static ProviderCharge Charge(int minutes, decimal cost) => new(Start.AddMinutes(minutes), cost);

    private static ProviderReconciliationReport Reconcile(
        IReadOnlyList<ProviderBalanceSnapshot> readings,
        IReadOnlyList<ProviderCharge> charges,
        ProviderDriftThreshold? threshold = null) =>
        ProviderReconciler.Reconcile("deepseek", Start, Start.AddDays(1), readings, charges, threshold ?? ProviderDriftThreshold.Default);

    [Fact]
    public void EachStretch_SetsTheBalancesFall_BesideTheChargesRecordedInIt()
    {
        var report = Reconcile(
            [Reading(0, 100m), Reading(5, 98m), Reading(10, 97m)],
            [Charge(1, 1.5m), Charge(5, 0.5m), Charge(7, 1m)]);

        report.Unavailable.Should().BeNull();
        report.ProviderSpendUsd.Should().Be(3m);
        report.GatewaySpendUsd.Should().Be(3m, "a charge at the instant of a reading counts in the stretch it ends");
        report.DifferenceUsd.Should().Be(0m);
        report.Drifting.Should().BeFalse();
        report.CoveredFrom.Should().Be(Start);
        report.CoveredTo.Should().Be(Start.AddMinutes(10));
    }

    [Fact]
    public void ChargesBeforeTheFirstReading_OrAfterTheLast_CountOnNeitherSide()
    {
        var report = Reconcile(
            [Reading(10, 50m), Reading(20, 49m)],
            [Charge(5, 7m), Charge(15, 1m), Charge(25, 7m)]);

        report.GatewaySpendUsd.Should().Be(1m);
        report.ProviderSpendUsd.Should().Be(1m);
    }

    [Fact]
    public void AStretchWithATopUp_IsLeftOutOnBothSides()
    {
        var report = Reconcile(
            [Reading(0, 10m), Reading(5, 9m), Reading(10, 59m), Reading(15, 58m)],
            [Charge(3, 1m), Charge(8, 4m), Charge(12, 1m)]);

        report.TopUps.Should().Be(1);
        report.ProviderSpendUsd.Should().Be(2m);
        report.GatewaySpendUsd.Should().Be(2m, "the 4 USD charged during the top-up stretch is not compared");
    }

    [Fact]
    public void AGatewayThatCountsTooMuch_Drifts_WithANegativeDifference()
    {
        // The case that started this: a rate card thirty times DeepSeek's price.
        var report = Reconcile([Reading(0, 220m), Reading(60, 219m)], [Charge(30, 29m)]);

        report.DifferenceUsd.Should().Be(-28m);
        report.DifferencePercent.Should().Be(96.55m);
        report.Drifting.Should().BeTrue();
    }

    [Fact]
    public void ASmallGap_UnderTheDollarFloor_IsNotFlagged_HoweverLargeTheShare()
    {
        var report = Reconcile([Reading(0, 10m), Reading(60, 9.9m)], [Charge(30, 0.5m)]);

        report.DifferencePercent.Should().Be(80m);
        report.Drifting.Should().BeFalse();

        Reconcile([Reading(0, 10m), Reading(60, 9.9m)], [Charge(30, 0.5m)], new ProviderDriftThreshold(10m, 0.1m))
            .Drifting.Should().BeTrue();
    }

    [Fact]
    public void ADayIsCreditedWithTheStretchesThatEndInIt()
    {
        var report = Reconcile(
            [Reading(-10, 100m), Reading(10, 99m), Reading(60 * 25, 97m)],
            [Charge(5, 1m), Charge(60 * 24, 2m)]);

        report.Daily.Should().Equal(
            new ProviderReconciliationDay(new DateOnly(2026, 10, 14), 1m, 1m),
            new ProviderReconciliationDay(new DateOnly(2026, 10, 15), 2m, 2m));
    }

    [Fact]
    public void FewerThanTwoReadings_OrAnotherCurrency_SayWhyThereIsNoComparison()
    {
        Reconcile([Reading(0, 10m)], []).Unavailable.Should().Contain("two balance readings");
        Reconcile([Reading(0, 10m, "CNY"), Reading(5, 9m, "CNY")], [])
            .Unavailable.Should().Contain("currency");
    }

    [Fact]
    public void NothingSpentOnEitherSide_HasNoPercentage_AndDoesNotDrift()
    {
        var report = Reconcile([Reading(0, 10m), Reading(5, 10m)], []);

        report.DifferencePercent.Should().BeNull();
        report.Drifting.Should().BeFalse();
    }
}
