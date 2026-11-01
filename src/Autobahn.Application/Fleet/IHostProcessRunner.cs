namespace Autobahn.Application.Fleet;

public interface IHostProcessRunner
{
    Task<string> RunAsync(string executable, IEnumerable<string> arguments, CancellationToken cancellationToken, int timeoutSeconds = 30);
}
