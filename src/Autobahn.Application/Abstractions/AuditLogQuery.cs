namespace Autobahn.Application.Abstractions;
/// <param name = "Limit">Most records to return.</param>
/// <param name = "ActionPrefix">Only actions that start with this (ordinal); null for all.</param>
/// <param name = "Cursor">Where to resume; null for the newest page.</param>
/// <param name = "MaxScanned">Most records to read while looking.</param>
public sealed record AuditLogQuery(int Limit, string? ActionPrefix = null, AuditLogCursor? Cursor = null, int MaxScanned = 20_000);
