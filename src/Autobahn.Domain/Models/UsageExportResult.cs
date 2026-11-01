using Autobahn.Domain.Billing;

namespace Autobahn.Domain.Models;
public sealed class UsageExportResult
{
    public required string ContentType { get; init; }
    public required string Body { get; init; }
    public required string FileName { get; init; }
    /// <summary>True when the export hit its row cap and rows were dropped.</summary>
    public bool Truncated { get; init; }
}
