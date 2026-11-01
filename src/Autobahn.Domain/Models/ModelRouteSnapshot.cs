namespace Autobahn.Domain.Models;
/// <summary>
/// The persisted route table plus the version it was read at. Mutations echo the version back on
/// write so a concurrent writer (another admin, another replica) is detected instead of silently
/// overwritten — the route table is rewritten wholesale, so a lost update loses whole routes.
/// </summary>
public sealed record ModelRouteSnapshot(IReadOnlyList<ModelConfig> Models, long Version)
{
    public static ModelRouteSnapshot Empty { get; } = new([], 0);
}
