using Autobahn.Domain.Models;

namespace Autobahn.Application.Abstractions;
/// <summary>
/// Buffers records off the request path and writes them to the archive in batches.
/// </summary>
public interface IGatewayErrorArchiveWriter
{
    void Enqueue(GatewayErrorRecord record);
    /// <summary>Writes everything buffered. Reads call this so a query cannot miss a just-recorded error.</summary>
    Task FlushPendingAsync(CancellationToken cancellationToken = default);
    /// <summary>
    /// Drops everything buffered without writing it. Used by clear-all so records captured before
    /// the wipe cannot land in the database after it.
    /// </summary>
    void DiscardPending();
    /// <summary>Records dropped unwritten because the buffer was full. Lifetime; reset by <see cref = "DiscardPending"/>.</summary>
    long DroppedTotal { get; }

    /// <summary>Records that failed to write after their retry. Lifetime; reset by <see cref = "DiscardPending"/>.</summary>
    long PersistFailedTotal { get; }
}
