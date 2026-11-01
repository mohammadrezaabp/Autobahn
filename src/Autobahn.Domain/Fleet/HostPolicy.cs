using Autobahn.Domain.Common;

namespace Autobahn.Domain.Fleet;

public sealed record HostPolicy
{
    public bool Enabled { get; init; }
    public int[] Gpus { get; init; } = [];
    public int[] SharedGpus { get; init; } = [];
    public string[] Images { get; init; } = ["vllm/vllm-openai"];
    public string[] MayStop { get; init; } = [];
    public int[] Ports { get; init; } = [8000, 8999];

    public void EnsureAllowed(ServerSpecification spec, IEnumerable<ServerSpecification> running)
    {
        spec.Validate();
        if (!Enabled) throw new DomainException("servers_disabled", "Model server management is disabled on this host.");
        if (spec.Gpus.Except(Gpus).Any()) throw new DomainException("gpu_forbidden", "A selected GPU is outside this host's policy.");
        if (Ports.Length != 2 || spec.Port < Ports[0] || spec.Port > Ports[1])
            throw new DomainException("port_forbidden", "The port is outside this host's allowed range.");
        var repository = spec.Image.Split('@')[0];
        var colon = repository.LastIndexOf(':');
        if (colon > repository.LastIndexOf('/')) repository = repository[..colon];
        if (!Images.Contains(repository, StringComparer.Ordinal))
            throw new DomainException("image_forbidden", "The image repository is not allowed on this host.");
        if (spec.Shared && spec.Gpus.Except(SharedGpus).Any())
            throw new DomainException("sharing_forbidden", "A selected GPU does not allow sharing.");
        foreach (var other in running.Where(x => x.Name != spec.Name))
        {
            if (other.Port == spec.Port) throw new DomainException("port_conflict", $"Port {spec.Port} is used by {other.Name}.");
            if (other.Gpus.Intersect(spec.Gpus).Any() && !(other.Shared && spec.Shared))
                throw new DomainException("gpu_conflict", $"A selected GPU is used by {other.Name}.");
        }
    }
}
