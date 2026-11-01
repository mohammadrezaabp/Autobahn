using Microsoft.Extensions.Options;
using Autobahn.Application.Abstractions;
using Autobahn.Domain.Configuration;

namespace Autobahn.Host.DependencyInjection;

/// <summary>
/// Writes the spent rate-limit token buckets to the database on an interval and on graceful
/// shutdown, and seats them again on startup.
/// </summary>
/// <remarks>
/// <para>Without it every restart handed every caller a full bucket, so a deploy during a busy
/// period let each key send its whole burst again at once. The database is the durable copy; the
/// in-memory buckets stay the hot path, so no request waits for a write.</para>
///
/// <para>Registered only when a database connection string is configured, after
/// <c>AddGatewayPersistence</c> so migrations run first. Like the stats snapshot, the restore races
/// the HTTP server for a moment at startup: a partition that takes a request before the restore
/// reaches it keeps the full bucket it was given. After an ungraceful kill, up to one interval of
/// spending is forgiven. Open streams and escalated Retry-After values are not carried over.</para>
///
/// <para>During a zero-downtime deploy two processes write the same rows for a short while. The
/// one that keeps serving writes last, which is the one whose buckets matter.</para>
/// </remarks>
internal sealed class RateLimitBucketSnapshotService(
    IServiceScopeFactory scopeFactory,
    IRateLimitBucketSnapshotSource source,
    IOptions<RateLimitingOptions> options,
    TimeProvider timeProvider,
    ILogger<RateLimitBucketSnapshotService> logger) : IHostedService
{
    private Task? _flushLoop;
    private CancellationTokenSource? _cts;

    private int IntervalSeconds => options.Value.BucketSnapshotIntervalSeconds;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (IntervalSeconds <= 0)
        {
            return;
        }

        await RestoreAsync(cancellationToken).ConfigureAwait(false);

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _flushLoop = RunFlushLoopAsync(_cts.Token);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_cts is null)
        {
            return;
        }

        await _cts.CancelAsync().ConfigureAwait(false);

        if (_flushLoop is not null)
        {
            try
            {
                await _flushLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }

        // Final write so the next process starts from what this one ended with.
        await FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    internal async Task RestoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IRateLimitBucketSnapshotStore>();

            var buckets = await store.LoadAsync(cancellationToken).ConfigureAwait(false);
            var restored = source.RestoreBuckets(buckets, timeProvider.GetUtcNow());

            logger.LogInformation(
                "Restored {Restored} of {Stored} rate-limit bucket(s) from the database.",
                restored,
                buckets.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Starting with full buckets is the behaviour before this existed; it must not block startup.
            logger.LogWarning(ex, "Failed to restore rate-limit buckets; every bucket starts full.");
        }
    }

    private async Task RunFlushLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(IntervalSeconds), timeProvider);

        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            await FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    internal async Task FlushAsync(CancellationToken cancellationToken)
    {
        try
        {
            var buckets = source.ExportSpentBuckets(
                timeProvider.GetUtcNow(),
                Math.Max(1, options.Value.BucketSnapshotMaxBuckets));

            await using var scope = scopeFactory.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IRateLimitBucketSnapshotStore>();
            await store.ReplaceAsync(buckets, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to write the rate-limit bucket snapshot to the database.");
        }
    }
}
