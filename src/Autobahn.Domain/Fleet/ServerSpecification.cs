using System.Text.RegularExpressions;
using Autobahn.Domain.Common;

namespace Autobahn.Domain.Fleet;

public sealed record ServerSpecification
{
    public required string Name { get; init; }
    public required string Model { get; init; }
    public string Revision { get; init; } = "main";
    public required int[] Gpus { get; init; }
    public required int Port { get; init; }
    public required string Image { get; init; }
    public required string ServedName { get; init; }
    public string[] Args { get; init; } = [];
    public bool ApiKey { get; init; } = true;
    public bool Shared { get; init; }

    public void Validate()
    {
        if (!Regex.IsMatch(Name, "^[a-z][a-z0-9-]{0,62}$"))
            throw new DomainException("invalid_name", "Server names must start with a letter and contain lowercase letters, numbers or hyphens.");
        ModelReference.Validate(Model);
        if (string.IsNullOrWhiteSpace(Revision) || Revision.StartsWith('-') || Revision.Contains('\0'))
            throw new DomainException("invalid_revision", "A valid model revision is required.");
        if (Gpus.Length == 0 || Gpus.Any(x => x < 0) || Gpus.Distinct().Count() != Gpus.Length)
            throw new DomainException("invalid_gpus", "Choose distinct GPU indexes.");
        if (Port is < 1024 or > 65535)
            throw new DomainException("invalid_port", "Choose an unprivileged TCP port.");
        if (!Regex.IsMatch(Image, "^[a-zA-Z0-9][a-zA-Z0-9._:/@-]{0,255}$") || string.IsNullOrWhiteSpace(ServedName))
            throw new DomainException("invalid_server", "An image and served model name are required.");
        // The application owns these flags. An operator cannot override host, mount or credential boundaries.
        string[] reserved = ["--host", "--port", "--api-key", "--model", "--served-model-name", "--download-dir", "--trust-remote-code"];
        if (Args.Any(arg => arg.Contains('\0') || reserved.Any(flag => arg == flag || arg.StartsWith(flag + "=", StringComparison.Ordinal))))
            throw new DomainException("reserved_argument", "The supplied arguments override an agent-managed setting.");
    }
}
