using Autobahn.Host;
using Serilog;
using Autobahn.Infrastructure.Fleet;
using Autobahn.Host.Fleet;

var builder = WebApplication.CreateBuilder(args);
builder.ConfigureGatewayHost();

builder.Services
    .AddGatewayCore(builder.Configuration, builder.Environment)
    .AddGatewayHealthChecks();

builder.Services.AddOpenApi();
builder.Services.AddFleet(builder.Configuration).AddFleetEndpoints(builder.Configuration);

var app = builder.Build();
app.ConfigureGatewayPipeline();
app.MapFleetEndpoints();

try
{
    Log.Information("Starting Autobahn gateway");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Gateway host terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}
