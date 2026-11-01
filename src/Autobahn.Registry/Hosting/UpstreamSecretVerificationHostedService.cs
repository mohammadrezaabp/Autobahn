using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Autobahn.Application.Abstractions;
using Autobahn.Registry.Services;

namespace Autobahn.Registry.Hosting;

/// <summary>
/// Checks at startup that every stored upstream credential can still be decrypted.
/// </summary>
/// <remarks>
/// The key pepper is the encryption key for the upstream secrets file, and rotating it — the
/// documented response to a leaked pepper — makes every previously stored credential permanently
/// unrecoverable. Without this check the only symptom was every request to every authenticated
/// upstream returning "Upstream auth token not configured", one request at a time, with nothing
/// pointing at the rotation as the cause. Reported at startup instead, where an operator can act on
/// it before the instance takes traffic.
/// </remarks>
public sealed class UpstreamSecretVerificationHostedService(
    FileUpstreamSecretStore secretStore,
    IModelRegistry registry,
    ILogger<UpstreamSecretVerificationHostedService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Runs after the registry loader. If that load failed there is no telling which entries are
        // in use, so every stored credential is judged rather than none.
        if (!registry.IsLoaded)
        {
            var (stored, failed) = secretStore.VerifyStoredSecrets();
            if (failed > 0)
            {
                logger.LogError(
                    "{Undecryptable} of {Total} stored upstream credential(s) cannot be decrypted with the "
                    + "configured Gateway:Security:KeyPepper. Models relying on them will fail every request "
                    + "with 'Upstream auth token not configured'. This is the expected result of rotating the "
                    + "pepper: re-enter the affected models' upstream API keys in the admin UI.",
                    failed,
                    stored);
            }

            return Task.CompletedTask;
        }

        var verification = secretStore.VerifyStoredSecrets(registry.GetAllModels());

        if (verification.Undecryptable.Count > 0)
        {
            logger.LogError(
                "{Undecryptable} of {Total} stored upstream credential(s) cannot be decrypted with the "
                + "configured Gateway:Security:KeyPepper: {ModelIds}. These models will fail every request "
                + "with 'Upstream auth token not configured'. This is the expected result of rotating the "
                + "pepper: re-enter the affected models' upstream API keys in the admin UI.",
                verification.Undecryptable.Count,
                verification.Total,
                string.Join(", ", verification.Undecryptable));
        }
        else if (verification.Total > 0)
        {
            logger.LogInformation("Verified {Total} stored upstream credential(s).", verification.Total);
        }

        if (verification.Unreferenced.Count > 0)
        {
            logger.LogWarning(
                "{Unreferenced} stored upstream credential(s) belong to no registered model and are ignored: "
                + "{ModelIds}. They are left in the secrets file.",
                verification.Unreferenced.Count,
                string.Join(", ", verification.Unreferenced));
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
