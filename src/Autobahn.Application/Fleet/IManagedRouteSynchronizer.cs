using Autobahn.Domain.Fleet;

namespace Autobahn.Application.Fleet;

public interface IManagedRouteSynchronizer
{
    Task PublishAsync(string host, ServerSpecification server, CancellationToken cancellationToken);
    Task RemoveAsync(string host, ServerSpecification server, CancellationToken cancellationToken);
}
