using Autobahn.Domain.Configuration;

namespace Autobahn.Application.Abstractions;

public interface ICorsConfigAdminService
{
    string[] GetCurrent();

    Task<CorsConfigUpdateResult> UpdateAsync(
        IReadOnlyList<string> allowedOrigins,
        CancellationToken cancellationToken = default);
}
