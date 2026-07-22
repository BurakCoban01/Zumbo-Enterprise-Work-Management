using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Zumbo.GatewayTests;

public sealed class GatewayAcceptanceTests(GatewayUpstreamFixture upstream)
    : IClassFixture<GatewayUpstreamFixture>
{
    private const string FrontendOrigin = "https://frontend.test";

    [Fact]
    public async Task ApiRoute_PreservesCorrelationSecurityHeadersAndCookies()
    {
        using (var directClient = new HttpClient())
        {
            var directResponse = await directClient.GetAsync(upstream.BaseUrl + "api/echo?value=direct");
            Assert.True(
                directResponse.IsSuccessStatusCode,
                $"{upstream.BaseUrl} {await directResponse.Content.ReadAsStringAsync()}");
        }

        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/echo?value=route");
        request.Headers.Add("Origin", FrontendOrigin);
        request.Headers.Add("X-Correlation-Id", "gateway-correlation-1");
        request.Headers.Add("Cookie", "zumbo-refresh=opaque-refresh");

        var response = await client.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, responseBody);
        var payload = JsonSerializer.Deserialize<EchoResponse>(responseBody, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/api/echo", payload!.Path);
        Assert.Equal("route", payload.Value);
        Assert.Equal("gateway-correlation-1", payload.CorrelationId);
        Assert.Equal("opaque-refresh", payload.RefreshCookie);
        Assert.Equal("gateway-correlation-1", response.Headers.GetValues("X-Correlation-Id").Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-store, max-age=0", response.Headers.GetValues("Cache-Control").Single());
        Assert.Equal(
            "default-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'none'; object-src 'none'",
            response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("same-origin", response.Headers.GetValues("Cross-Origin-Opener-Policy").Single());
        Assert.Equal("same-origin", response.Headers.GetValues("Cross-Origin-Resource-Policy").Single());
        Assert.Equal("camera=(), microphone=(), geolocation=()", response.Headers.GetValues("Permissions-Policy").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.Contains("upstream-session=opaque", cookie, StringComparison.Ordinal);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnknownHost_IsRejectedBeforeProxying()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://untrusted-host.test")
        });

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/echo")).StatusCode);
    }

    [Fact]
    public async Task CredentialedCorsPreflight_AllowsOnlyConfiguredOrigin()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var allowed = new HttpRequestMessage(HttpMethod.Options, "/api/echo");
        allowed.Headers.Add("Origin", FrontendOrigin);
        allowed.Headers.Add("Access-Control-Request-Method", "GET");
        var allowedResponse = await client.SendAsync(allowed);

        Assert.Equal(HttpStatusCode.NoContent, allowedResponse.StatusCode);
        Assert.Equal(FrontendOrigin, allowedResponse.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal("true", allowedResponse.Headers.GetValues("Access-Control-Allow-Credentials").Single());

        using var rejected = new HttpRequestMessage(HttpMethod.Options, "/api/echo");
        rejected.Headers.Add("Origin", "https://attacker.test");
        rejected.Headers.Add("Access-Control-Request-Method", "GET");
        var rejectedResponse = await client.SendAsync(rejected);
        Assert.False(rejectedResponse.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task HubAccessToken_IsMovedToAuthorizationAndRemovedFromProxiedQuery()
    {
        const string secret = "gateway.header.payload.signature";
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetFromJsonAsync<EchoResponse>(
            $"/hubs/token-probe?access_token={secret}&value=preserved");

        Assert.NotNull(response);
        Assert.Equal("?value=preserved", response.QueryString);
        Assert.Equal($"Bearer {secret}", response.Authorization);
    }

    [Fact]
    public async Task ProductionGateway_AddsHstsAndRejectsWildcardHostConfiguration()
    {
        await using (var gateway = await GatewayKestrelHost.StartAsync(upstream.BaseUrl, FrontendOrigin))
        using (var client = new HttpClient { BaseAddress = new Uri(gateway.BaseUrl) })
        {
            var response = await client.GetAsync("/");
            Assert.Equal("max-age=31536000; includeSubDomains", response.Headers.GetValues("Strict-Transport-Security").Single());
        }

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "*",
            ["Cors:AllowedOrigins:0"] = FrontendOrigin,
            ["Gateway:UpstreamBaseUrl"] = upstream.BaseUrl
        });
        Assert.Throws<InvalidOperationException>(() => GatewayHost.AddServices(builder));
    }

    [Fact]
    public void MultiReplicaConfiguration_PreservesDestinationsAndValidatesPolicy()
    {
        var options = new GatewayOptions
        {
            UpstreamBaseUrls = ["http://api-1:8080", "http://api-2:8080/"],
            LoadBalancingPolicy = "PowerOfTwoChoices"
        };

        options.Validate();
        var config = new GatewayProxyConfigProvider(Options.Create(options)).GetConfig();
        var cluster = Assert.Single(config.Clusters);
        Assert.Equal("PowerOfTwoChoices", cluster.LoadBalancingPolicy);
        Assert.Equal(
            ["http://api-1:8080/", "http://api-2:8080/"],
            cluster.Destinations!.OrderBy(x => x.Key).Select(x => x.Value.Address));

        Assert.Throws<InvalidOperationException>(() => new GatewayOptions
        {
            UpstreamBaseUrls = ["http://api:8080", "http://api:8080/"],
            LoadBalancingPolicy = "RoundRobin"
        }.Validate());
        Assert.Throws<InvalidOperationException>(() => new GatewayOptions
        {
            UpstreamBaseUrls = ["http://api:8080"],
            LoadBalancingPolicy = "StickySession"
        }.Validate());
    }

    [Fact]
    public async Task NotificationExtractionRoute_IsSpecificAndRollbackReturnsItToMonolith()
    {
        await using var extracted = await GatewayUpstreamFixture.StartAsync("notifications-extracted");
        using (var extractedFactory = CreateFactory(new Dictionary<string, string?>
               {
                   ["Gateway:NotificationExtractionEnabled"] = "true",
                   ["Gateway:NotificationUpstreamBaseUrl"] = extracted.BaseUrl
               }))
        using (var client = extractedFactory.CreateClient())
        {
            var notificationRoot = await client.GetFromJsonAsync<SourceResponse>("/api/notifications");
            var notification = await client.GetFromJsonAsync<SourceResponse>("/api/notifications/probe");
            var project = await client.GetFromJsonAsync<SourceResponse>("/api/projects/probe");

            Assert.Equal("notifications-extracted", notificationRoot!.Source);
            Assert.Equal("notifications-extracted", notification!.Source);
            Assert.Equal("monolith", project!.Source);
            Assert.Equal("/api/notifications/probe", notification.Path);
        }

        using var rollbackFactory = CreateFactory(new Dictionary<string, string?>
        {
            ["Gateway:NotificationExtractionEnabled"] = "false",
            ["Gateway:NotificationUpstreamBaseUrl"] = extracted.BaseUrl
        });
        using var rollbackClient = rollbackFactory.CreateClient();
        var rolledBack = await rollbackClient.GetFromJsonAsync<SourceResponse>("/api/notifications/probe");
        Assert.Equal("monolith", rolledBack!.Source);

        var enabled = new GatewayOptions
        {
            UpstreamBaseUrl = upstream.BaseUrl,
            NotificationExtractionEnabled = true,
            NotificationUpstreamBaseUrl = extracted.BaseUrl
        };
        var enabledConfig = new GatewayProxyConfigProvider(Options.Create(enabled)).GetConfig();
        Assert.Contains(enabledConfig.Routes, route =>
            route.RouteId == "notifications-extraction"
            && route.ClusterId == "zumbo-notifications"
            && route.Order == -100);
        Assert.Throws<InvalidOperationException>(() => new GatewayOptions
        {
            UpstreamBaseUrl = upstream.BaseUrl,
            NotificationExtractionEnabled = true
        }.Validate());
    }

    [Fact]
    public async Task OversizedBody_IsRejectedBeforeUpstream()
    {
        using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["Gateway:MaxRequestBodyBytes"] = "64"
        });
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/body", new StringContent(new string('x', 65)));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(0, upstream.BodyRequestCount);
    }

    [Fact]
    public async Task EdgeRateLimit_IsAppliedPerClientPartition()
    {
        using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["Gateway:PermitLimit"] = "2",
            ["Gateway:RateWindowSeconds"] = "60"
        });
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/echo")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/echo")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/api/echo")).StatusCode);
    }

    [Fact]
    public async Task TimeoutAndUnavailableUpstream_ReturnControlledGatewayFailures()
    {
        using (var timeoutFactory = CreateFactory(new Dictionary<string, string?>
               {
                   ["Gateway:UpstreamTimeoutSeconds"] = "1"
               }))
        using (var timeoutClient = timeoutFactory.CreateClient())
        {
            var timeout = await timeoutClient.GetAsync("/api/delay");
            Assert.Equal(HttpStatusCode.GatewayTimeout, timeout.StatusCode);
        }

        using var unavailableFactory = CreateFactory(new Dictionary<string, string?>
        {
            ["Gateway:UpstreamBaseUrl"] = $"http://127.0.0.1:{UnusedLoopbackPort()}/",
            ["Gateway:UpstreamTimeoutSeconds"] = "1"
        });
        using var unavailableClient = unavailableFactory.CreateClient();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await unavailableClient.GetAsync("/health/ready")).StatusCode);
        Assert.Contains(
            (await unavailableClient.GetAsync("/api/echo")).StatusCode,
            new[] { HttpStatusCode.BadGateway, HttpStatusCode.ServiceUnavailable, HttpStatusCode.GatewayTimeout });
    }

    [Fact]
    public async Task Retry_IsLimitedToIdempotentRequests()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/retry")).StatusCode);
        Assert.Equal(2, upstream.IdempotentRetryRequestCount);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsync("/api/retry", null)).StatusCode);
        Assert.Equal(1, upstream.UnsafeRetryRequestCount);
    }

    [Fact]
    public async Task RealtimeRoute_UpgradesAndRelaysSignalRFramesOnKestrel()
    {
        await using var gateway = await GatewayKestrelHost.StartAsync(upstream.BaseUrl, FrontendOrigin);
        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Origin", FrontendOrigin);

        await socket.ConnectAsync(new Uri(gateway.BaseUrl + "hubs/signalr").ToWebSocketUri(), CancellationToken.None);
        await SendSignalRFrame(socket, "{\"protocol\":\"json\",\"version\":1}");
        Assert.Equal("{}\u001e", await ReceiveTextMessage(socket));

        await SendSignalRFrame(
            socket,
            "{\"type\":1,\"invocationId\":\"1\",\"target\":\"Echo\",\"arguments\":[\"signalr-frame\"]}");
        var completion = await ReceiveTextMessage(socket);

        Assert.Contains("\"type\":3", completion, StringComparison.Ordinal);
        Assert.Contains("\"invocationId\":\"1\"", completion, StringComparison.Ordinal);
        Assert.Contains("\"result\":\"signalr-frame\"", completion, StringComparison.Ordinal);

        using var rejectedSocket = new ClientWebSocket();
        rejectedSocket.Options.SetRequestHeader("Origin", "https://attacker.test");
        await Assert.ThrowsAsync<WebSocketException>(() => rejectedSocket.ConnectAsync(
            new Uri(gateway.BaseUrl + "hubs/signalr").ToWebSocketUri(),
            CancellationToken.None));
    }

    private static Task SendSignalRFrame(ClientWebSocket socket, string payload)
    {
        var bytes = Encoding.UTF8.GetBytes(payload + '\u001e');
        return socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private static async Task<string> ReceiveTextMessage(ClientWebSocket socket)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var buffer = new byte[1024];
        using var message = new MemoryStream();
        WebSocketReceiveResult received;
        do
        {
            received = await socket.ReceiveAsync(buffer, timeout.Token);
            Assert.Equal(WebSocketMessageType.Text, received.MessageType);
            await message.WriteAsync(buffer.AsMemory(0, received.Count), timeout.Token);
        }
        while (!received.EndOfMessage);

        return Encoding.UTF8.GetString(message.ToArray());
    }

    private WebApplicationFactory<Program> CreateFactory(IReadOnlyDictionary<string, string?>? overrides = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                var settings = new Dictionary<string, string?>
                {
                    ["Gateway:UpstreamBaseUrl"] = upstream.BaseUrl,
                    ["Gateway:UpstreamTimeoutSeconds"] = "5",
                    ["Gateway:PermitLimit"] = "1000",
                    ["Cors:AllowedOrigins:0"] = FrontendOrigin
                };
                if (overrides is not null)
                {
                    foreach (var (key, value) in overrides)
                    {
                        settings[key] = value;
                    }
                }

                configuration.AddInMemoryCollection(settings);
            }));

    private static int UnusedLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed record EchoResponse(
        string Path,
        string? Value,
        string? CorrelationId,
        string? RefreshCookie,
        string? Authorization,
        string? QueryString);

    private sealed record SourceResponse(string Source, string Path);
}

public sealed class GatewayUpstreamFixture : IAsyncLifetime
{
    private readonly string identity;
    private WebApplication? _app;
    private int _bodyRequestCount;
    private int _idempotentRetryRequestCount;
    private int _unsafeRetryRequestCount;

    public string BaseUrl { get; private set; } = string.Empty;
    public int BodyRequestCount => Volatile.Read(ref _bodyRequestCount);
    public int IdempotentRetryRequestCount => Volatile.Read(ref _idempotentRetryRequestCount);
    public int UnsafeRetryRequestCount => Volatile.Read(ref _unsafeRetryRequestCount);

    public GatewayUpstreamFixture() : this("monolith")
    {
    }

    private GatewayUpstreamFixture(string identity)
    {
        this.identity = identity;
    }

    public static async Task<GatewayUpstreamFixture> StartAsync(string identity)
    {
        var fixture = new GatewayUpstreamFixture(identity);
        await fixture.InitializeAsync();
        return fixture;
    }

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Services.AddSignalR();
        var app = builder.Build();
        app.UseWebSockets();
        app.MapGet("/health/ready", () => Results.Ok());
        app.MapGet("/api/notifications/{**path}", (HttpContext context) => Results.Json(new
        {
            source = identity,
            path = context.Request.Path.Value
        }));
        app.MapGet("/api/projects/{**path}", (HttpContext context) => Results.Json(new
        {
            source = identity,
            path = context.Request.Path.Value
        }));
        app.MapGet("/api/echo", (HttpContext context, string? value) =>
        {
            context.Response.Cookies.Append("upstream-session", "opaque", new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict
            });
            return Results.Json(new
            {
                path = context.Request.Path.Value,
                value,
                correlationId = context.Request.Headers["X-Correlation-Id"].ToString(),
                refreshCookie = context.Request.Cookies["zumbo-refresh"],
                authorization = context.Request.Headers.Authorization.ToString(),
                queryString = context.Request.QueryString.Value
            });
        });
        app.MapGet("/hubs/token-probe", (HttpContext context, string? value) => Results.Json(new
        {
            path = context.Request.Path.Value,
            value,
            correlationId = context.Request.Headers["X-Correlation-Id"].ToString(),
            refreshCookie = context.Request.Cookies["zumbo-refresh"],
            authorization = context.Request.Headers.Authorization.ToString(),
            queryString = context.Request.QueryString.Value
        }));
        app.MapPost("/api/body", async (HttpContext context) =>
        {
            Interlocked.Increment(ref _bodyRequestCount);
            await context.Request.Body.CopyToAsync(Stream.Null);
            return Results.Ok();
        });
        app.MapGet("/api/delay", async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5));
            return Results.Ok();
        });
        app.MapMethods("/api/retry", ["GET", "POST"], (HttpContext context) =>
        {
            if (HttpMethods.IsGet(context.Request.Method))
            {
                var attempt = Interlocked.Increment(ref _idempotentRetryRequestCount);
                return attempt == 1 ? Results.StatusCode(StatusCodes.Status503ServiceUnavailable) : Results.Ok();
            }

            Interlocked.Increment(ref _unsafeRetryRequestCount);
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        });
        app.MapHub<SignalREchoHub>("/hubs/signalr");

        await app.StartAsync();
        var addresses = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses;
        BaseUrl = addresses.Single().TrimEnd('/') + "/";
        _app = app;
    }

    public async Task DisposeAsync()
    {
        if (_app is null)
        {
            return;
        }

        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

public sealed class SignalREchoHub : Hub
{
    public string Echo(string message) => message;
}

internal sealed class GatewayKestrelHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    private GatewayKestrelHost(WebApplication app, string baseUrl)
    {
        _app = app;
        BaseUrl = baseUrl;
    }

    public string BaseUrl { get; }

    public static async Task<GatewayKestrelHost> StartAsync(string upstreamBaseUrl, string frontendOrigin)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing"
        });
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "localhost;127.0.0.1",
            ["Gateway:UpstreamBaseUrl"] = upstreamBaseUrl,
            ["Gateway:UpstreamTimeoutSeconds"] = "5",
            ["Gateway:PermitLimit"] = "1000",
            ["Cors:AllowedOrigins:0"] = frontendOrigin
        });
        GatewayHost.AddServices(builder);

        var app = builder.Build();
        GatewayHost.ConfigurePipeline(app);
        await app.StartAsync();
        var addresses = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses;
        var baseUrl = addresses.Single().TrimEnd('/') + "/";
        return new GatewayKestrelHost(app, baseUrl);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

internal static class UriExtensions
{
    public static Uri ToWebSocketUri(this Uri source)
    {
        var builder = new UriBuilder(source)
        {
            Scheme = source.Scheme == Uri.UriSchemeHttps ? "wss" : "ws"
        };
        return builder.Uri;
    }
}
