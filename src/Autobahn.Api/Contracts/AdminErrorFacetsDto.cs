using Autobahn.Domain.Models;

namespace Autobahn.Api.Contracts;
public sealed class AdminErrorFacetsDto
{
    public required IReadOnlyList<AdminErrorFacetValueDto> Models { get; init; }
    public required IReadOnlyList<AdminErrorFacetValueDto> StatusCodes { get; init; }
    public required IReadOnlyList<AdminErrorFacetValueDto> ErrorCodes { get; init; }
    public required IReadOnlyList<AdminErrorFacetValueDto> Levels { get; init; }

    public static AdminErrorFacetsDto From(GatewayErrorFacets facets) => new()
    {
        Models = [..facets.Models.Select(AdminErrorFacetValueDto.From)],
        StatusCodes = [..facets.Statuses.Select(AdminErrorFacetValueDto.From)],
        ErrorCodes = [..facets.Codes.Select(AdminErrorFacetValueDto.From)],
        Levels = [..facets.Levels.Select(AdminErrorFacetValueDto.From)],
    };
}
