using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Autobahn.Api.Contracts;
using Autobahn.Application.Abstractions;
using Autobahn.Domain.Models;
using Autobahn.Application.Security;

namespace Autobahn.Api.Endpoints;
/// <summary>Query-string filters shared by the group, occurrence and export endpoints.</summary>
public sealed class AdminErrorFilter
{
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public string? Level { get; init; }
    public string? ModelId { get; init; }
    public int? Status { get; init; }
    public string? Code { get; init; }
    public string? TenantId { get; init; }
    public string? RequestId { get; init; }
    public string? Fingerprint { get; init; }
    public string? Search { get; init; }
    public string? Sort { get; init; }
    public int? Limit { get; init; }
    public int? Offset { get; init; }

    public GatewayErrorQuery ToQuery() => new GatewayErrorQuery
    {
        From = From,
        To = To,
        MinimumLevel = GatewayLogLevels.ParseFilter(Level),
        ModelId = ModelId,
        StatusCode = Status,
        EventCode = Code,
        TenantId = TenantId,
        RequestId = RequestId,
        Fingerprint = Fingerprint,
        Search = Search,
        Sort = GatewayErrorQuery.ParseSort(Sort),
        Limit = Limit ?? GatewayErrorQuery.DefaultLimit,
        Offset = Offset ?? 0,
    }.Clamp();
}
