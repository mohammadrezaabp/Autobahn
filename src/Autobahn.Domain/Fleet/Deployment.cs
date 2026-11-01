using Autobahn.Domain.Common;

namespace Autobahn.Domain.Fleet;

public sealed class Deployment : IAggregateRoot
{
    private Deployment() { }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public required string Host { get; init; }
    public required string BaseRevision { get; init; }
    public ServerSpecification[] Add { get; init; } = [];
    public string[] Remove { get; init; } = [];
    public DeploymentStatus Status { get; private set; }
    public string? Error { get; private set; }

    public static Deployment Draft(string host, string revision, ServerSpecification[] add, string[] remove)
    {
        if (string.IsNullOrWhiteSpace(host) || add.Length + remove.Length == 0)
            throw new DomainException("empty_change", "Choose a host and at least one server change.");
        foreach (var spec in add) spec.Validate();
        if (add.Select(x => x.Name).Distinct().Count() != add.Length || remove.Distinct().Count() != remove.Length)
            throw new DomainException("duplicate_server", "Server names in a change must be unique.");
        return new Deployment { Host = host, BaseRevision = revision, Add = add, Remove = remove };
    }

    public void Propose()
    {
        if (Status != DeploymentStatus.Draft) throw new DomainException("invalid_transition", "Only draft changes can be proposed.");
        Status = DeploymentStatus.Proposed;
    }

    public void BeginApply(string currentRevision)
    {
        if (Status is not (DeploymentStatus.Draft or DeploymentStatus.Proposed))
            throw new DomainException("invalid_transition", "This deployment cannot be applied again.");
        if (BaseRevision != currentRevision) throw new DomainException("stale_inventory", "The inventory changed. Review a new plan.");
        Status = DeploymentStatus.Applying;
    }

    public void Complete()
    {
        if (Status != DeploymentStatus.Applying) throw new DomainException("invalid_transition", "The deployment is not applying.");
        Status = DeploymentStatus.Applied;
    }

    public void Fail(string reason)
    {
        if (Status != DeploymentStatus.Applying) throw new DomainException("invalid_transition", "The deployment is not applying.");
        Status = DeploymentStatus.Failed;
        Error = reason;
    }
}
