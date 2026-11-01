using Breaker = Autobahn.Policy.CircuitBreaker.CircuitBreaker;
using Autobahn.Policy.CircuitBreaker;

namespace Autobahn.Policy.Tests.CircuitBreaker;

/// <summary>
/// The breaker opens for every caller of a model, so one caller's failures are not enough while
/// another caller is being answered.
/// </summary>
public sealed class CircuitBreakerCallerTests
{
    private static readonly CircuitBreakerPolicyOptions Policy = new()
    {
        FailureThreshold = 3,
        BreakDuration = TimeSpan.FromMinutes(1),
        MinFailingCallers = 2,
    };

    [Fact]
    public void RecordFailure_OneCallerFailingWhileAnotherSucceeds_StaysClosed()
    {
        var sut = new Breaker(Policy);

        sut.RecordSuccess("key-b");
        for (var i = 0; i < 10; i++)
        {
            sut.RecordFailure("key-a");
        }

        sut.State.Should().Be(CircuitState.Closed);
    }

    [Fact]
    public void RecordFailure_TwoCallersFailing_Opens()
    {
        var sut = new Breaker(Policy);

        sut.RecordFailure("key-a");
        sut.RecordFailure("key-a");
        sut.State.Should().Be(CircuitState.Closed);

        sut.RecordFailure("key-b");

        sut.State.Should().Be(CircuitState.Open);
    }

    /// <summary>With nobody else on the model there is no second opinion to wait for.</summary>
    [Fact]
    public void RecordFailure_OnlyCallerOfTheModel_Opens()
    {
        var sut = new Breaker(Policy);

        for (var i = 0; i < 3; i++)
        {
            sut.RecordFailure("key-a");
        }

        sut.State.Should().Be(CircuitState.Open);
    }

    [Fact]
    public void RecordFailure_OtherCallerLeftTheWindow_Opens()
    {
        var now = DateTimeOffset.UtcNow;
        var sut = new Breaker(Policy, () => now);

        sut.RecordSuccess("key-b");
        now = now.AddMinutes(5);

        for (var i = 0; i < 3; i++)
        {
            sut.RecordFailure("key-a");
        }

        sut.State.Should().Be(CircuitState.Open);
    }

    [Fact]
    public void RecordFailure_UnattributedFailure_CountsAsCorroborated()
    {
        var sut = new Breaker(Policy);

        sut.RecordSuccess("key-b");
        sut.RecordFailure("key-a");
        sut.RecordFailure("key-a");
        sut.RecordFailure("key-a");
        sut.State.Should().Be(CircuitState.Closed);

        sut.RecordFailure();

        sut.State.Should().Be(CircuitState.Open);
    }

    [Fact]
    public void RecordFailure_MinFailingCallersOfOne_IgnoresWhoFailed()
    {
        var sut = new Breaker(new CircuitBreakerPolicyOptions
        {
            FailureThreshold = 3,
            FailureRatioThreshold = 0.5,
            MinFailingCallers = 1,
        });

        sut.RecordSuccess("key-b");
        for (var i = 0; i < 3; i++)
        {
            sut.RecordFailure("key-a");
        }

        sut.State.Should().Be(CircuitState.Open);
    }

    /// <summary>A failed probe is one request, and it is enough: the breaker was already open.</summary>
    [Fact]
    public void RecordFailure_HalfOpenProbe_ReopensWhoeverSentIt()
    {
        var now = DateTimeOffset.UtcNow;
        var sut = new Breaker(Policy, () => now);
        sut.RecordFailure("key-a");
        sut.RecordFailure("key-a");
        sut.RecordFailure("key-b");
        now = now.AddMinutes(2);
        sut.TryEnter().Should().BeTrue();

        sut.RecordFailure("key-a");

        sut.State.Should().Be(CircuitState.Open);
    }
}
