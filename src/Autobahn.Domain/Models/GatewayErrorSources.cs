namespace Autobahn.Domain.Models;
/// <summary>Where a page of errors was served from, so the UI can say so when there is no database.</summary>
public static class GatewayErrorSources
{
    public const string Database = "database";
    public const string Memory = "memory";
}
