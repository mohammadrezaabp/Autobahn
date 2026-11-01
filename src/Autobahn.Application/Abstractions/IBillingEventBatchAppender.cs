using Autobahn.Domain.Billing;
using Autobahn.Domain.Models;

namespace Autobahn.Application.Abstractions;
/// <summary>
/// Optional batch contract for <see cref = "IBillingEventRepository"/> implementations that can
/// append many events in one transaction. The usage writer prefers it when present and falls back
/// to <see cref = "IBillingEventRepository.TryAppendAsync"/> per event otherwise.
/// </summary>
/// <remarks>
/// Kept separate from the repository interface so a "batch" that still commits row by row is never
/// silently accepted as batching: an implementation either provides one probe and one commit per
/// batch, or it does not claim to.
/// </remarks>
public interface IBillingEventBatchAppender
{
    /// <summary>
    /// Appends <paramref name = "records"/> idempotently and returns the subset that was actually
    /// inserted (in input order). Records whose <c>RequestId</c> already exists are skipped.
    /// </summary>
    Task<IReadOnlyList<BillingEventRecord>> TryAppendManyAsync(IReadOnlyList<BillingEventRecord> records, CancellationToken cancellationToken = default);
}
