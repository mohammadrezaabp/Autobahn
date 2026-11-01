using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;
using Autobahn.Host.Panels;
using Autobahn.Integration.Tests.Support;

namespace Autobahn.Integration.Tests.Admin;

/// <summary>
/// The Grafana and Prometheus panels under <c>/admin</c>: who is let through, and what reaches the
/// tool behind them.
/// </summary>
public sealed class AdminPanelsEndpointTests : IAsyncLifetime
{
    private readonly EchoUpstream _upstream = new();
    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        await _upstream.StartAsync();
        _factory = GatewayWebApplicationFactory.CreateWithInMemoryDatabase(configureSettings: settings =>
        {
            settings["Gateway:Panels:GrafanaUrl"] = _upstream.Address;
            settings["Gateway:Panels:PrometheusUrl"] = _upstream.Address;
        });
        await GatewayWebApplicationFactory.EnsureAuthReadyAsync(_factory);
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _upstream.DisposeAsync();
    }

    [Fact]
    public async Task Panel_WithoutSessionOrKey_IsRefusedBeforeTheUpstreamIsCalled()
    {
        using var client = CreateBareClient();

        var response = await client.GetAsync("/admin/prometheus/graph");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _upstream.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task OpenSession_SetsOneCookiePerPanel_ScopedToItsPath()
    {
        var cookies = await OpenSessionAsync();

        cookies.Should().HaveCount(2);
        cookies.Should().OnlyContain(c =>
            c.StartsWith(PanelSessionTokens.CookieName + "=", StringComparison.Ordinal) &&
            c.Contains("httponly", StringComparison.OrdinalIgnoreCase) &&
            c.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase));
        cookies.Should().ContainSingle(c => c.Contains("path=/admin/grafana", StringComparison.OrdinalIgnoreCase));
        cookies.Should().ContainSingle(c => c.Contains("path=/admin/prometheus", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Prometheus_WithSession_IsForwardedWithoutThePrefixOrTheSessionCookie()
    {
        var session = SessionPair(await OpenSessionAsync());
        using var client = CreateBareClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/admin/prometheus/api/v1/query?query=up");
        request.Headers.Add("Cookie", $"theme=dark; {session}");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var seen = _upstream.Requests.Should().ContainSingle().Subject;
        seen.Path.Should().Be("/api/v1/query");
        seen.Query.Should().Be("?query=up");
        seen.Host.Should().Be("localhost");
        seen.Cookie.Should().Be("theme=dark");
    }

    [Fact]
    public async Task Grafana_WithSession_KeepsThePrefixItServesFrom()
    {
        var session = SessionPair(await OpenSessionAsync());
        using var client = CreateBareClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/admin/grafana/d/Autobahn-gateway/Autobahn-gateway");
        request.Headers.Add("Cookie", session);

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _upstream.Requests.Should().ContainSingle().Which.Path.Should().Be("/admin/grafana/d/Autobahn-gateway/Autobahn-gateway");
    }

    [Fact]
    public async Task Panel_WithSession_RefusesARequestStartedByAnotherOrigin()
    {
        var session = SessionPair(await OpenSessionAsync());
        using var client = CreateBareClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/admin/grafana/api/ds/query");
        request.Headers.Add("Cookie", session);
        request.Headers.Add("Sec-Fetch-Site", "same-site");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _upstream.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Panel_WithAnOperatorKeyHeader_IsForwardedWithoutTheKey()
    {
        using var client = _factory.CreateAdminClient();

        var response = await client.GetAsync("/admin/prometheus/api/v1/targets");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var seen = _upstream.Requests.Should().ContainSingle().Subject;
        seen.Path.Should().Be("/api/v1/targets");
        seen.ApiKey.Should().BeEmpty();
    }

    [Fact]
    public async Task Panel_WithoutATrailingSlash_IsRedirectedToItsRoot()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-API-Key", GatewayWebApplicationFactory.DefaultAdminApiKey);

        var response = await client.GetAsync("/admin/prometheus");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be("/admin/prometheus/");
        _upstream.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Panel_WithATamperedSession_IsRefused()
    {
        var session = SessionPair(await OpenSessionAsync());
        using var client = CreateBareClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/admin/grafana/");
        request.Headers.Add("Cookie", session.Replace("=", "=9", StringComparison.Ordinal));

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// The cookie opens the panels and nothing else. The admin API still wants the key in a header,
    /// which is what keeps a page on another origin from driving it.
    /// </summary>
    [Fact]
    public async Task AdminApi_DoesNotAcceptThePanelSession()
    {
        var session = SessionPair(await OpenSessionAsync());
        using var client = CreateBareClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/admin/api/panels");
        request.Headers.Add("Cookie", session);

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task List_ReportsEachConfiguredPanelAndWhetherItAnswers()
    {
        using var client = _factory.CreateAdminClient();

        using var body = JsonDocument.Parse(await client.GetStringAsync("/admin/api/panels"));

        var panels = body.RootElement.GetProperty("panels").EnumerateArray().ToArray();
        panels.Select(p => p.GetProperty("path").GetString()).Should().Equal("/admin/grafana/", "/admin/prometheus/");
        panels.Should().OnlyContain(p => p.GetProperty("reachable").GetBoolean());
        _upstream.Requests.Select(r => r.Path).Should().BeEquivalentTo("/admin/grafana/api/health", "/-/healthy");
    }

    [Fact]
    public async Task List_AddsALogsLinkInsideGrafana_WhenALogsDashboardIsConfigured()
    {
        await using var factory = GatewayWebApplicationFactory.CreateWithInMemoryDatabase(configureSettings: settings =>
        {
            settings["Gateway:Panels:GrafanaUrl"] = _upstream.Address;
            settings["Gateway:Panels:LogsDashboard"] = "/d/Autobahn-logs/Autobahn-logs";
        });
        await GatewayWebApplicationFactory.EnsureAuthReadyAsync(factory);
        using var client = factory.CreateAdminClient();

        using var body = JsonDocument.Parse(await client.GetStringAsync("/admin/api/panels"));

        var panels = body.RootElement.GetProperty("panels").EnumerateArray().ToArray();
        panels.Select(p => p.GetProperty("id").GetString()).Should().Equal("grafana", "logs");
        panels[1].GetProperty("label").GetString().Should().Be("Logs");
        panels[1].GetProperty("path").GetString().Should().Be("/admin/grafana/d/Autobahn-logs/Autobahn-logs");
        panels[1].GetProperty("reachable").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task List_HasNoLogsLink_WhenGrafanaIsNotServed()
    {
        await using var factory = GatewayWebApplicationFactory.CreateWithInMemoryDatabase(configureSettings: settings =>
        {
            settings["Gateway:Panels:PrometheusUrl"] = _upstream.Address;
            settings["Gateway:Panels:LogsDashboard"] = "d/Autobahn-logs/Autobahn-logs";
        });
        await GatewayWebApplicationFactory.EnsureAuthReadyAsync(factory);
        using var client = factory.CreateAdminClient();

        using var body = JsonDocument.Parse(await client.GetStringAsync("/admin/api/panels"));

        body.RootElement.GetProperty("panels").EnumerateArray()
            .Select(p => p.GetProperty("id").GetString()).Should().Equal("prometheus");
    }

    [Fact]
    public async Task Panel_ThatIsNotConfigured_IsNotFound()
    {
        await using var factory = GatewayWebApplicationFactory.CreateWithInMemoryDatabase();
        await GatewayWebApplicationFactory.EnsureAuthReadyAsync(factory);
        using var client = factory.CreateAdminClient();

        var response = await client.GetAsync("/admin/grafana/");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public void SessionToken_ExpiresAndIsBoundToTheProcessThatIssuedIt()
    {
        var clock = new ManualClock();
        var tokens = new PanelSessionTokens(clock);
        var token = tokens.Issue(TimeSpan.FromMinutes(10));

        tokens.IsValid(token).Should().BeTrue();
        new PanelSessionTokens(clock).IsValid(token).Should().BeFalse();

        clock.Advance(TimeSpan.FromMinutes(11));
        tokens.IsValid(token).Should().BeFalse();
    }

    private HttpClient CreateBareClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });

    private async Task<string[]> OpenSessionAsync()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add("X-API-Key", GatewayWebApplicationFactory.DefaultAdminApiKey);

        var response = await client.PostAsync("/admin/api/panels/session", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        return response.Headers.GetValues("Set-Cookie").ToArray();
    }

    /// <summary>The <c>name=value</c> part of the first cookie; both carry the same value.</summary>
    private static string SessionPair(string[] setCookies) => setCookies[0].Split(';')[0];

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed record SeenRequest(string Path, string Query, string Host, string Cookie, string ApiKey);

    /// <summary>Stands in for Grafana and Prometheus: answers 200 to anything and records what it was sent.</summary>
    private sealed class EchoUpstream : IAsyncDisposable
    {
        private readonly List<SeenRequest> _requests = [];
        private WebApplication? _app;

        public string Address { get; private set; } = string.Empty;

        public IReadOnlyList<SeenRequest> Requests
        {
            get
            {
                lock (_requests)
                {
                    return _requests.ToArray();
                }
            }
        }

        public async Task StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            _app = builder.Build();
            _app.Run(context =>
            {
                lock (_requests)
                {
                    _requests.Add(new SeenRequest(
                        context.Request.Path.Value ?? string.Empty,
                        context.Request.QueryString.Value ?? string.Empty,
                        context.Request.Host.Value ?? string.Empty,
                        context.Request.Headers.Cookie.ToString(),
                        context.Request.Headers["X-API-Key"].ToString()));
                }

                return context.Response.WriteAsync("ok");
            });
            await _app.StartAsync();
            Address = _app.Urls.First();
        }

        public async ValueTask DisposeAsync()
        {
            if (_app is not null)
            {
                await _app.DisposeAsync();
            }
        }
    }
}
