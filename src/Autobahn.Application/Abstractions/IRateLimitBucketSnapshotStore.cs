using Autobahn.Domain.RateLimiting;

namespace Autobahn.Application.Abstractions;

/// <summary>
/// Persists the spent token buckets (see <see cref="RateLimitBucketSnapshot"/>). Registered only
/// when a database connection string is configured.
/// </summary>
public interface IRateLimitBucketSnapshotStore
{
    Task<IReadOnlyList<RateLimitBucketSnapshot>> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Makes the stored set equal to <paramref name="buckets"/>: rows not in it are removed.</summary>
    Task ReplaceAsync(IReadOnlyList<RateLimitBucketSnapshot> buckets, CancellationToken cancellationToken = default);
}
