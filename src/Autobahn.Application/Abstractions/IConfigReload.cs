using Autobahn.Domain.Models;

namespace Autobahn.Application.Abstractions;

public interface IConfigReload
{
    bool IsReloadInProgress { get; }

    Task<ConfigReloadResult> ReloadAsync(CancellationToken cancellationToken = default);

    ConfigStatusResponse GetStatus();
}
