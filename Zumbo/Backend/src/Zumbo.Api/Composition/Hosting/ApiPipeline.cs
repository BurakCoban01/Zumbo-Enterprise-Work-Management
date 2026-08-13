using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;

internal static class ApiPipeline
{
    internal static WebApplication UseZumboPipeline(this WebApplication app)
    {
        app.UseForwardedHeaders();
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseMiddleware<AccessTokenRedactionMiddleware>();
        app.UseMiddleware<RequestTelemetryMiddleware>();
        app.UseMiddleware<ApiExceptionMiddleware>();
        app.UseMiddleware<RequestAbuseProtectionMiddleware>();
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }
        app.UseCors("LocalFrontends");
        app.UseMiddleware<BrowserSessionSecurityMiddleware>();
        app.UseAuthentication();
        if ((app.Configuration["RateLimiting:Provider"] ?? "InMemory")
            .Equals("Redis", StringComparison.OrdinalIgnoreCase))
        {
            app.UseMiddleware<RedisRateLimitingMiddleware>();
        }
        else
        {
            app.UseRateLimiter();
        }
        app.UseAuthorization();
        app.UseMiddleware<EndpointPermissionMiddleware>();
        return app;
    }

    internal static WebApplication MapZumboEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
        var realtime = app.Configuration.GetSection("Realtime").Get<WorkItemRealtimeOptions>()
            ?? new WorkItemRealtimeOptions();
        realtime.Validate();
        app.MapHub<WorkItemHub>("/hubs/work-items", options =>
        {
            options.ApplicationMaxBufferSize = realtime.ApplicationMaxBufferBytes;
            options.TransportMaxBufferSize = realtime.TransportMaxBufferBytes;
            options.AllowStatefulReconnects = true;
        })
            .RequireRateLimiting("realtime-connect")
            .WithZumboPermission(PermissionCatalog.WorkItemView);

        var api = app.MapGroup("/api").RequireRateLimiting("api");
        app.MapControllers();
        if (app.Environment.IsDevelopment())
        {
            app.MapGet("/", () => Results.Redirect("/swagger"));
        }
        else
        {
            app.MapGet("/", (IConfiguration configuration) => Results.Ok(new
            {
                service = "Zumbo.Api",
                status = "ready",
                instanceId = configuration["Runtime:InstanceId"] ?? Environment.MachineName
            }));
        }
        return app;
    }
}
