namespace Autobahn.Application.Fleet;

public sealed class FleetException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
