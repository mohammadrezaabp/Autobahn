using System.Diagnostics;
using Autobahn.Application.Fleet;

namespace Autobahn.Infrastructure.Agent;

public sealed class ProcessRunner : IHostProcessRunner
{
    public async Task<string> RunAsync(string executable, IEnumerable<string> arguments, CancellationToken cancellationToken, int timeoutSeconds = 30)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        var info = new ProcessStartInfo(executable) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = info };
        try
        {
            process.Start();
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { if (!process.HasExited) process.Kill(true); throw; }
            var result = await output; var stderr = await error;
            if (process.ExitCode != 0)
            {
                if (stderr.Contains("Driver/library version mismatch", StringComparison.OrdinalIgnoreCase) || result.Contains("Driver/library version mismatch", StringComparison.OrdinalIgnoreCase))
                    throw new FleetException(503, "gpu_driver_mismatch", "The NVIDIA kernel driver and user-space library have different versions. Restart the host after aligning the driver packages.");
                throw new FleetException(502, "host_command_failed", $"{Path.GetFileName(executable)} returned exit code {process.ExitCode}.");
            }
            return result;
        }
        catch (System.ComponentModel.Win32Exception) { throw new FleetException(503, "host_tool_missing", $"Install {Path.GetFileName(executable)} on this host."); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new FleetException(504, "host_command_timeout", $"{Path.GetFileName(executable)} timed out."); }
    }
}
