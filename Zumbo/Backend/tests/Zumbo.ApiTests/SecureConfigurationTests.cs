using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Zumbo.Modules.Audit;
using Zumbo.Modules.Boards;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Notifications;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.Projects;
using Zumbo.Modules.Teams;
using Zumbo.Modules.Workflows;
using Zumbo.Modules.WorkItems;

namespace Zumbo.ApiTests;

public sealed class SecureConfigurationTests(WebApplicationFactory<Program> baseFactory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string FrontendOrigin = "https://frontend.test";

    [Fact]
    public void ProductionDefaults_FailBeforeListening()
    {
        using var factory = baseFactory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));

        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }

    [Fact]
    public void UnknownProvider_FailsBeforeListening()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(Program).Assembly.GetName().Name,
            EnvironmentName = "Development"
        });
        var settings = ProductionSettings();
        settings["Search:Provider"] = "UnknownSearch";
        builder.Configuration.AddInMemoryCollection(settings);

        Assert.Throws<InvalidOperationException>(() => builder.AddZumboHost());
    }

    [Fact]
    public async Task ProductionHttpSurface_GatesSwaggerAndAppliesExactSecurityPolicy()
    {
        await using var app = await CreateProductionHttpAppAsync();
        using var client = app.GetTestClient();
        client.BaseAddress = new Uri("https://api.test");

        var root = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, root.StatusCode);
        var cacheControl = Header(root, "Cache-Control");
        Assert.Contains("private", cacheControl, StringComparison.Ordinal);
        Assert.Contains("no-store", cacheControl, StringComparison.Ordinal);
        Assert.Contains("max-age=0", cacheControl, StringComparison.Ordinal);
        Assert.Equal(
            "default-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'none'; object-src 'none'",
            Header(root, "Content-Security-Policy"));
        Assert.Equal("same-origin", Header(root, "Cross-Origin-Opener-Policy"));
        Assert.Equal("same-origin", Header(root, "Cross-Origin-Resource-Policy"));
        Assert.Equal("camera=(), microphone=(), geolocation=()", Header(root, "Permissions-Policy"));
        Assert.Equal("no-referrer", Header(root, "Referrer-Policy"));
        Assert.Equal("max-age=31536000; includeSubDomains", Header(root, "Strict-Transport-Security"));
        Assert.Equal("nosniff", Header(root, "X-Content-Type-Options"));
        Assert.Equal("DENY", Header(root, "X-Frame-Options"));
        Assert.Equal("sec005-api", Header(root, "X-Zumbo-Instance-Id"));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/swagger/index.html")).StatusCode);

        using var allowed = CorsPreflight(FrontendOrigin);
        var allowedResponse = await client.SendAsync(allowed);
        Assert.Equal(HttpStatusCode.NoContent, allowedResponse.StatusCode);
        Assert.Equal(FrontendOrigin, Header(allowedResponse, "Access-Control-Allow-Origin"));
        Assert.Equal("true", Header(allowedResponse, "Access-Control-Allow-Credentials"));

        using var rejected = CorsPreflight("https://attacker.test");
        var rejectedResponse = await client.SendAsync(rejected);
        Assert.False(rejectedResponse.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task HubAccessToken_IsRemovedFromQueryBeforeTelemetry()
    {
        const string secret = "header.payload.signature";
        string? observedQuery = null;
        string? observedAuthorization = null;
        var context = new DefaultHttpContext();
        context.Request.Path = "/hubs/work-items";
        context.Request.QueryString = new QueryString($"?access_token={secret}&id=42");
        var logger = new CapturingLogger<RequestTelemetryMiddleware>();
        var telemetry = new RequestTelemetryMiddleware(
            _ => Task.CompletedTask,
            logger);
        var redaction = new AccessTokenRedactionMiddleware(async http =>
        {
            observedQuery = http.Request.QueryString.Value;
            observedAuthorization = http.Request.Headers.Authorization;
            await telemetry.InvokeAsync(http);
        });

        await redaction.InvokeAsync(context);

        Assert.Equal("?id=42", observedQuery);
        Assert.Equal($"Bearer {secret}", observedAuthorization);
        Assert.DoesNotContain(secret, logger.Messages.Single(), StringComparison.Ordinal);
    }

    private static async Task<WebApplication> CreateProductionHttpAppAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(Program).Assembly.GetName().Name,
            EnvironmentName = "Production"
        });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(ProductionSettings());
        builder.AddZumboHost();
        builder.Services
            .AddIdentityModule()
            .AddOrganizationsModule()
            .AddTeamsModule()
            .AddProjectsModule()
            .AddBoardsModule()
            .AddNotificationsModule(builder.Configuration)
            .AddAuditModule()
            .AddWorkflowsModule()
            .AddWorkItemsModule()
            .AddSprintsModule();
        builder.Services.RemoveAll<IHostedService>();

        var app = builder.Build();
        app.UseZumboPipeline();
        app.MapZumboEndpoints();
        await app.StartAsync();
        return app;
    }

    private static Dictionary<string, string?> ProductionSettings() => new()
    {
        ["AllowedHosts"] = "api.test;localhost",
        ["BackgroundJobs:Enabled"] = "false",
        ["Cors:AllowedOrigins:0"] = FrontendOrigin,
        ["DataProtection:KeyPath"] = Path.Combine(Path.GetTempPath(), "zumbo-sec005-keys"),
        ["DistributedLock:Provider"] = "Redis",
        ["DistributedLock:Redis:ConnectionString"] = "127.0.0.1:1,abortConnect=false",
        ["Jwt:ActiveKeyId"] = "primary",
        ["Jwt:SigningKey"] = "sec005-production-test-signing-key-material-with-more-than-64-characters",
        ["Persistence:Provider"] = "PostgreSql",
        ["PostgreSql:ConnectionString"] = "Host=127.0.0.1;Port=1;Database=zumbo;Username=test;Password=test-only-password",
        ["ReadModelCache:Provider"] = "Redis",
        ["RateLimiting:Provider"] = "Redis",
        ["RateLimiting:Redis:ConnectionString"] = "127.0.0.1:1,abortConnect=false",
        ["RegistrationProvisioning:Mode"] = "ProductionLike",
        ["Realtime:Backplane"] = "Redis",
        ["Realtime:Redis:ConnectionString"] = "127.0.0.1:1,abortConnect=false",
        ["Search:Provider"] = "OpenSearch",
        ["Search:OpenSearch:BaseUrl"] = "https://127.0.0.1:1",
        ["Storage:Provider"] = "Minio",
        ["Storage:Minio:Endpoint"] = "http://127.0.0.1:1",
        ["Storage:Minio:AccessKey"] = "test-access-key",
        ["Storage:Minio:SecretKey"] = "test-secret-key",
        ["Storage:Minio:BucketName"] = "zumbo-sec005",
        ["Runtime:Role"] = "Api",
        ["Runtime:InstanceId"] = "sec005-api"
    };

    private static HttpRequestMessage CorsPreflight(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/browser-auth/session");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        return request;
    }

    private static string Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values)
            ? values.Single()
            : response.Content.Headers.GetValues(name).Single();

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
