using Autobahn.Application.Fleet;
using Autobahn.Domain.Fleet;

namespace Autobahn.Infrastructure.Agent;

public sealed class AgentOptions
{
    public string Name { get; set; } = Environment.MachineName;
    public string Token { get; set; } = "";
    public string ModelDirectory { get; set; } = "data/models";
    public string DataDirectory { get; set; } = "data/agent";
    public bool AllowDelete { get; set; }
    public long MinFreeBytes { get; set; } = 10L * 1024 * 1024 * 1024;
    public string NvidiaSmi { get; set; } = "nvidia-smi";
    public string Docker { get; set; } = "docker";
    public int ServerStartupTimeoutSeconds { get; set; } = 900;
    public string? HuggingFaceToken { get; set; }
    public HostPolicy ServerPolicy { get; set; } = new();
    public FleetNode[] Peers { get; set; } = [];
}
