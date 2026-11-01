namespace Autobahn.Domain.Models.Overview;
/// <summary>A console deep link: the tab plus the hash parameters it understands.</summary>
public sealed record AttentionLink(string Tab, IReadOnlyDictionary<string, string>? Params = null);
