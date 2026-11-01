using Autobahn.Billing.Usage;

namespace Autobahn.Billing.Tests.Usage;

public sealed class ProviderSpendLedgerTests
{
    private static readonly string Key = ProviderSpendLedger.KeyScope(Guid.Parse("11111111-1111-1111-1111-111111111111"), "openrouter");
    private static readonly string Tenant = ProviderSpendLedger.TenantScope(Guid.Parse("22222222-2222-2222-2222-222222222222"), "openrouter");

    [Fact]
    public void TryReserve_HoldsTheAmountInEveryScope_UntilReleased()
    {
        var ledger = new ProviderSpendLedger(TimeSpan.FromMinutes(5));

        ledger.TryReserve("r1", 2m, [(Key, 10m), (Tenant, 100m)]).Should().BeNull();

        ledger.GetOutstanding(Key).Should().Be(2m);
        ledger.GetOutstanding(Tenant).Should().Be(2m);

        ledger.Release("r1");

        ledger.GetOutstanding(Key).Should().Be(0m);
        ledger.GetOutstanding(Tenant).Should().Be(0m);
    }

    /// <summary>The reason the ledger exists: requests in flight together share one headroom.</summary>
    [Fact]
    public void TryReserve_RequestsInFlightTogether_CannotOvershootTheKeyCap()
    {
        var ledger = new ProviderSpendLedger(TimeSpan.FromMinutes(5));

        ledger.TryReserve("r1", 4m, [(Key, 10m), (Tenant, null)]).Should().BeNull();
        ledger.TryReserve("r2", 4m, [(Key, 10m), (Tenant, null)]).Should().BeNull();
        ledger.TryReserve("r3", 4m, [(Key, 10m), (Tenant, null)]).Should().Be(Key);

        ledger.GetOutstanding(Key).Should().Be(8m, "the refused request reserved nothing");
    }

    [Fact]
    public void TryReserve_RefusedByTheTenant_TakesNothingFromTheKeyEither()
    {
        var ledger = new ProviderSpendLedger(TimeSpan.FromMinutes(5));

        ledger.TryReserve("r1", 5m, [(Key, 100m), (Tenant, 3m)]).Should().Be(Tenant);

        ledger.GetOutstanding(Key).Should().Be(0m);
    }

    /// <summary>A model with no price estimates 0; a cap that is already used up must still refuse it.</summary>
    [Fact]
    public void TryReserve_AnExhaustedScope_RefusesEvenAZeroEstimate()
    {
        var ledger = new ProviderSpendLedger(TimeSpan.FromMinutes(5));

        ledger.TryReserve("r1", 0m, [(Key, 0m), (Tenant, null)]).Should().Be(Key);
        ledger.TryReserve("r2", 0m, [(Key, -0.5m), (Tenant, null)]).Should().Be(Key);
        ledger.TryReserve("r3", 0m, [(Key, 0.01m), (Tenant, null)]).Should().BeNull();
    }

    [Fact]
    public void TryReserve_WithNoCapAnywhere_AlwaysFits_AndIsStillRecorded()
    {
        var ledger = new ProviderSpendLedger(TimeSpan.FromMinutes(5));

        ledger.TryReserve("r1", 500m, [(Key, null), (Tenant, null)]).Should().BeNull();

        ledger.GetOutstanding(Tenant).Should().Be(500m);
    }

    [Fact]
    public void TryReserve_TheSameRequestTwice_ReservesOnce()
    {
        var ledger = new ProviderSpendLedger(TimeSpan.FromMinutes(5));

        ledger.TryReserve("r1", 2m, [(Key, 10m)]).Should().BeNull();
        ledger.TryReserve("r1", 2m, [(Key, 10m)]).Should().BeNull();

        ledger.GetOutstanding(Key).Should().Be(2m);
    }

    [Fact]
    public void AReservationThatIsNeverReleased_ExpiresWithItsTtl()
    {
        var now = DateTimeOffset.UtcNow;
        var ledger = new ProviderSpendLedger(TimeSpan.FromSeconds(60), () => now);
        ledger.TryReserve("lost", 9m, [(Key, 10m)]).Should().BeNull();

        now = now.AddSeconds(61);

        ledger.GetOutstanding(Key).Should().Be(0m);
        ledger.TryReserve("next", 9m, [(Key, 10m)]).Should().BeNull();
    }
}
