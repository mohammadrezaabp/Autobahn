namespace Autobahn.Domain.RateLimiting;
/// <param name = "Scope">The scope whose limit was hit.</param>
/// <param name = "Key">The partition inside that scope.</param>
/// <param name = "Control">Whether it was the rate bucket or a concurrency cap.</param>
/// <param name = "Hits">How many requests it refused in the window.</param>
public sealed record RateLimitViolationRow(string Scope, string Key, string Control, long Hits);
