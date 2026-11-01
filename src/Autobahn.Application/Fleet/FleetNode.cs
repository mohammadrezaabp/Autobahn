namespace Autobahn.Application.Fleet;

public sealed record FleetNode
{
    public string Id { get; init; } = "";
    public string Address { get; init; } = "";
    public string InferenceAddress { get; init; } = "";
    public string Token { get; init; } = "";
}
