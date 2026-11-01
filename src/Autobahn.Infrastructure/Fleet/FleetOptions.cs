using Autobahn.Application.Fleet;

namespace Autobahn.Infrastructure.Fleet;

public sealed class FleetOptions
{
    public string DataDirectory { get; set; } = "data/fleet";
    public int PollSeconds { get; set; } = 15;
    public int SessionHours { get; set; } = 8;
    public FleetNode[] Nodes { get; set; } = [];
    public GitOpsOptions GitOps { get; set; } = new();
}
