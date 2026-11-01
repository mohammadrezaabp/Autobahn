namespace Autobahn.Domain.Models;
/// <summary>
/// Thrown when a route write is attempted against a version that is no longer current, i.e. the
/// route table changed between the caller's read and its write.
/// </summary>
public sealed class ModelRouteVersionConflictException(long expectedVersion, long actualVersion) : Exception($"The model routes changed since they were read (expected version {expectedVersion}, " + $"found {actualVersion}).")
{
    public long ExpectedVersion { get; } = expectedVersion;
    public long ActualVersion { get; } = actualVersion;
}
