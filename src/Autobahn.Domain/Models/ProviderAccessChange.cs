namespace Autobahn.Domain.Models;
/// <summary>The result of a switch change, with the value it replaced for the audit log.</summary>
public sealed record ProviderAccessChange(string Provider, bool Open, bool PreviousOpen);
