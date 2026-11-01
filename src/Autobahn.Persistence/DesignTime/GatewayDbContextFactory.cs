using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Autobahn.Persistence.Infrastructure;

namespace Autobahn.Persistence.DesignTime;

public sealed class GatewayDbContextFactory : IDesignTimeDbContextFactory<GatewayDbContext>
{
    public GatewayDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("GATEWAY_DB_CONNECTION")
            ?? "Data Source=gateway.design.db";

        var options = new DbContextOptionsBuilder<GatewayDbContext>();
        SqliteGatewayDbContext.Configure(options, connectionString);

        return new GatewayDbContext(options.Options);
    }
}
