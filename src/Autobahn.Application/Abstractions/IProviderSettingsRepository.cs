using Autobahn.Domain.Billing;

namespace Autobahn.Application.Abstractions;
public interface IProviderSettingsRepository
{
    Task<IReadOnlyList<ProviderSettingsRecord>> ListAsync(CancellationToken cancellationToken = default);
    Task UpsertAsync(ProviderSettingsRecord settings, CancellationToken cancellationToken = default);
}
