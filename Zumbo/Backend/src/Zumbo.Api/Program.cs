using System.Net;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.IdentityModel.Tokens;
using Zumbo.BuildingBlocks.Infrastructure.Concurrency;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.BuildingBlocks.Infrastructure.Runtime;
using Zumbo.BuildingBlocks.Infrastructure.Search;
using Zumbo.BuildingBlocks.Infrastructure.Security;
using Zumbo.BuildingBlocks.Infrastructure.Storage;
using Zumbo.Modules.Audit;
using Zumbo.Modules.Boards;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Notifications;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.Projects;
using Zumbo.Modules.Teams;
using Zumbo.Modules.WorkItems;
using Zumbo.Modules.Workflows;
using Zumbo.SharedKernel;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(30));

builder.Configuration
    .AddJsonFile("appsettings.Identity.json", optional: true)
    .AddJsonFile("appsettings.Organizations.json", optional: true)
    .AddJsonFile("appsettings.Teams.json", optional: true)
    .AddJsonFile("appsettings.Projects.json", optional: true)
    .AddJsonFile("appsettings.Boards.json", optional: true)
    .AddJsonFile("appsettings.WorkItems.json", optional: true)
    .AddJsonFile("appsettings.Workflows.json", optional: true)
    .AddEnvironmentVariables();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpContextAccessor();
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 26 * 1024 * 1024;
    options.ValueLengthLimit = 16 * 1024;
    options.MultipartHeadersLengthLimit = 16 * 1024;
});
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    foreach (var value in builder.Configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [])
    {
        if (IPAddress.TryParse(value, out var address))
        {
            options.KnownProxies.Add(address);
        }
    }
});
builder.Services.AddCors(options =>
{
    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    options.AddPolicy("LocalFrontends", policy =>
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod());
});
var rateLimits = builder.Configuration.GetSection("RateLimiting").Get<RateLimitingOptions>() ?? new RateLimitingOptions();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        var http = context.HttpContext;
        http.Response.Headers["X-Correlation-Id"] = http.TraceIdentifier;
        await http.Response.WriteAsJsonAsync(
            ApiResponse<object>.Fail(
                "RATE_LIMIT_EXCEEDED",
                "Too many requests. Retry after the rate-limit window resets.",
                http.TraceIdentifier),
            cancellationToken);
    };
    options.AddPolicy("login", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = Math.Clamp(rateLimits.LoginPermitLimit, 3, 1000),
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
    options.AddPolicy("password-reset", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = Math.Clamp(rateLimits.PasswordResetPermitLimit, 1, 100),
                Window = TimeSpan.FromMinutes(15),
                QueueLimit = 0
            }));
    options.AddPolicy("api", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? context.Connection.RemoteIpAddress?.ToString()
                ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = Math.Clamp(rateLimits.ApiPermitLimit, 30, 10_000),
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
    options.AddPolicy("search", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? context.Connection.RemoteIpAddress?.ToString()
                ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = Math.Clamp(rateLimits.SearchPermitLimit, 10, 5_000),
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
    options.AddPolicy("upload", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? context.Connection.RemoteIpAddress?.ToString()
                ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = Math.Clamp(rateLimits.UploadPermitLimit, 1, 1_000),
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
    options.AddPolicy("realtime-connect", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? context.Connection.RemoteIpAddress?.ToString()
                ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = Math.Clamp(rateLimits.RealtimeConnectPermitLimit, 10, 10_000),
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<LoginSecurityOptions>(builder.Configuration.GetSection("LoginSecurity"));
builder.Services.Configure<IdentityBootstrapOptions>(builder.Configuration.GetSection("IdentityBootstrap"));
builder.Services.Configure<PasswordResetOptions>(builder.Configuration.GetSection("PasswordReset"));
builder.Services.Configure<EmailNotificationOptions>(builder.Configuration.GetSection("Notifications:Email"));
builder.Services.Configure<DueDateReminderOptions>(builder.Configuration.GetSection("Notifications:DueDateReminder"));
builder.Services.Configure<WorkItemReadModelCacheOptions>(builder.Configuration.GetSection("ReadModelCache"));
builder.Services.Configure<MongoDbSettings>(builder.Configuration.GetSection("MongoDb"));
builder.Services.Configure<PersistenceOptions>(builder.Configuration.GetSection("Persistence"));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection("Storage"));
builder.Services.Configure<LocalStorageOptions>(builder.Configuration.GetSection("Storage:Local"));
builder.Services.Configure<MinioStorageOptions>(builder.Configuration.GetSection("Storage:Minio"));
builder.Services.Configure<SearchOptions>(builder.Configuration.GetSection("Search"));
builder.Services.Configure<OpenSearchOptions>(builder.Configuration.GetSection("Search:OpenSearch"));

var jwtOptions = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = ZumboAuthenticationSchemes.Smart;
        options.DefaultChallengeScheme = ZumboAuthenticationSchemes.Smart;
    })
    .AddPolicyScheme(ZumboAuthenticationSchemes.Smart, ZumboAuthenticationSchemes.Smart, options =>
    {
        options.ForwardDefaultSelector = context =>
            context.Request.Headers.ContainsKey("X-API-Key")
                ? ZumboAuthenticationSchemes.ApiKey
                : JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrWhiteSpace(accessToken)
                    && context.HttpContext.Request.Path.StartsWithSegments("/hubs/work-items"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            },
            OnTokenValidated = async context =>
            {
                var userId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? context.Principal?.FindFirstValue("sub");
                var securityStamp = context.Principal?.FindFirstValue("securityStamp");
                var sessionId = context.Principal?.FindFirstValue("sessionId");
                if (string.IsNullOrWhiteSpace(userId)
                    || string.IsNullOrWhiteSpace(securityStamp)
                    || string.IsNullOrWhiteSpace(sessionId))
                {
                    context.Fail("Token session claims are missing.");
                    return;
                }

                var repository = context.HttpContext.RequestServices.GetRequiredService<IUserRepository>();
                var clock = context.HttpContext.RequestServices.GetRequiredService<IClock>();
                var user = await repository.GetByIdAsync(userId, context.HttpContext.RequestAborted);
                var sessionIsActive = user?.RefreshTokens.Any(x =>
                    x.SessionId == sessionId && x.IsActive(clock.UtcNow)) == true;

                if (user is null
                    || !user.IsActive
                    || !string.Equals(user.SecurityStamp, securityStamp, StringComparison.Ordinal)
                    || !sessionIsActive)
                {
                    context.Fail("User or token session is no longer active.");
                }
            }
        };
    })
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
        ZumboAuthenticationSchemes.ApiKey,
        _ => { });
builder.Services.AddAuthorization();

var dataProtectionKeyPath = builder.Configuration["DataProtection:KeyPath"];
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("Zumbo");
if (!string.IsNullOrWhiteSpace(dataProtectionKeyPath))
{
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeyPath));
}

var signalR = builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.MaximumReceiveMessageSize = 32 * 1024;
});
if (builder.Configuration.GetValue<string>("Realtime:Backplane")
        ?.Equals("Redis", StringComparison.OrdinalIgnoreCase) == true)
{
    var realtimeRedis = builder.Configuration["Realtime:Redis:ConnectionString"]
        ?? builder.Configuration["DistributedLock:Redis:ConnectionString"]
        ?? "localhost:6379,abortConnect=false";
    signalR.AddStackExchangeRedis(realtimeRedis, options =>
    {
        options.Configuration.ChannelPrefix = StackExchange.Redis.RedisChannel.Literal("zumbo:realtime");
    });
}

builder.Services.AddSingleton<IClock, Zumbo.BuildingBlocks.Infrastructure.Runtime.SystemClock>();
builder.Services.AddZumboDistributedLocking(builder.Configuration);
var readModelCacheProvider = builder.Configuration.GetValue<string>("ReadModelCache:Provider") ?? "InMemory";
if (readModelCacheProvider.Equals("Redis", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IWorkItemReadModelCache, RedisWorkItemReadModelCache>();
}
else
{
    builder.Services.AddSingleton<IWorkItemReadModelCache, InMemoryWorkItemReadModelCache>();
}
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
builder.Services.AddSingleton<ITokenIssuer, JwtTokenIssuer>();

var storageProvider = builder.Configuration.GetValue<string>("Storage:Provider") ?? "Local";
if (storageProvider.Equals("Minio", StringComparison.OrdinalIgnoreCase)
    || storageProvider.Equals("MinIO", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IFileStorage, MinioFileStorage>();
}
else
{
    builder.Services.AddSingleton<IFileStorage, LocalFileStorage>();
}

var runtimeRole = builder.Configuration.GetValue<string>("Runtime:Role") ?? "Api";
var isWorkerRole = runtimeRole.Equals("Worker", StringComparison.OrdinalIgnoreCase);
var searchProvider = builder.Configuration.GetValue<string>("Search:Provider") ?? "InMemory";
if (searchProvider.Equals("OpenSearch", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHttpClient<IWorkItemSearchIndex, OpenSearchWorkItemSearchIndex>();
    if (!isWorkerRole)
    {
        builder.Services.AddHostedService<SearchIndexInitializer>();
    }
}
else
{
    builder.Services.AddSingleton<IWorkItemSearchIndex, InMemoryWorkItemSearchIndex>();
}

var provider = builder.Configuration.GetValue<string>("Persistence:Provider") ?? "InMemory";
if (provider.Equals("Mongo", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IMongoDbService, MongoDbService>();
    builder.Services.AddScoped(typeof(IDocumentRepository<>), typeof(MongoRepository<>));
    if (!isWorkerRole)
    {
        builder.Services.AddHostedService<MongoIndexInitializer>();
    }
}
else
{
    builder.Services.AddSingleton(typeof(IDocumentRepository<>), typeof(InMemoryDocumentRepository<>));
}

var healthChecks = builder.Services.AddHealthChecks()
    .AddCheck<StorageHealthCheck>("storage", tags: ["ready"]);
if (provider.Equals("Mongo", StringComparison.OrdinalIgnoreCase))
{
    healthChecks.AddCheck<MongoHealthCheck>("mongodb", tags: ["ready"]);
}

if ((builder.Configuration.GetValue<string>("DistributedLock:Provider") ?? "InMemory")
    .Equals("Redis", StringComparison.OrdinalIgnoreCase))
{
    healthChecks.AddCheck<RedisHealthCheck>("redis", tags: ["ready"]);
}

builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IPasswordResetNotifier, PasswordResetNotifierAdapter>();
builder.Services.AddSingleton<IMfaSecretProtector, MfaSecretProtectorAdapter>();
builder.Services.AddScoped<IdentityService>();
builder.Services.AddScoped<ApiKeyService>();
builder.Services.AddScoped<IPrivacyDataProcessor, PrivacyDataProcessorAdapter>();
builder.Services.AddScoped<PrivacyService>();
builder.Services.AddScoped<IIdentityAuditWriter, IdentityAuditWriterAdapter>();
builder.Services.AddScoped<IdentityPermissionService>();
builder.Services.AddScoped<IdentityAdministrationService>();
builder.Services.AddScoped<IOrganizationMemberDirectory, OrganizationMemberDirectoryAdapter>();
builder.Services.AddScoped<OrganizationService>();
builder.Services.AddScoped<ITeamUserDirectory, TeamUserDirectoryAdapter>();
builder.Services.AddScoped<ITeamAuditWriter, TeamAuditWriterAdapter>();
builder.Services.AddScoped<TeamService>();
builder.Services.AddScoped<IProjectMemberDirectory, ProjectMemberDirectoryAdapter>();
builder.Services.AddScoped<IProjectTeamDirectory, ProjectTeamDirectoryAdapter>();
builder.Services.AddScoped<IProjectTeamUsageChecker, ProjectTeamUsageCheckerAdapter>();
builder.Services.AddScoped<IProjectAuditWriter, ProjectAuditWriterAdapter>();
builder.Services.AddScoped<IBoardAuditWriter, BoardAuditWriterAdapter>();
builder.Services.AddScoped<IWorkflowAuditWriter, WorkflowAuditWriterAdapter>();
builder.Services.AddScoped<IOrganizationAuditWriter, OrganizationAuditWriterAdapter>();
builder.Services.AddScoped<ProjectService>();
builder.Services.AddScoped<IBoardProjectAccessChecker, BoardProjectAccessCheckerAdapter>();
builder.Services.AddScoped<BoardPolicyAdapter>();
builder.Services.AddScoped<IBoardColumnUsageChecker>(services => services.GetRequiredService<BoardPolicyAdapter>());
builder.Services.AddScoped<IBoardPlacementPolicy>(services => services.GetRequiredService<BoardPolicyAdapter>());
builder.Services.AddScoped<BoardService>();
builder.Services.AddScoped<INotificationUserDirectory, NotificationUserDirectoryAdapter>();
builder.Services.AddScoped<IEmailNotificationSender, SmtpEmailNotificationSender>();
builder.Services.AddScoped<NotificationService>();
if (builder.Configuration.GetValue("BackgroundJobs:Enabled", true))
{
    builder.Services.AddHostedService<NotificationEmailDispatcherHostedService>();
    builder.Services.AddHostedService<DueDateReminderHostedService>();
}
builder.Services.AddScoped<IAuditAccessChecker, AuditAccessCheckerAdapter>();
builder.Services.AddScoped<IAuditRequestContext, HttpAuditRequestContext>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<IWorkflowProjectAccessChecker, WorkflowProjectAccessCheckerAdapter>();
builder.Services.AddScoped<WorkflowService>();
builder.Services.AddScoped<IProjectPermissionChecker, ProjectPermissionCheckerAdapter>();
builder.Services.AddScoped<IWorkItemTeamPolicy, WorkItemTeamPolicyAdapter>();
builder.Services.AddScoped<IWorkflowPolicy, WorkflowPolicyAdapter>();
builder.Services.AddScoped<IAttachmentStorage, AttachmentStorageAdapter>();
builder.Services.AddScoped<IWorkItemRealtimePublisher, SignalRWorkItemRealtimePublisher>();
builder.Services.AddScoped<WorkItemService>();

var app = builder.Build();

app.UseForwardedHeaders();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<RequestTelemetryMiddleware>();
app.UseMiddleware<ApiExceptionMiddleware>();
app.UseSwagger();
app.UseSwaggerUI();
app.UseCors("LocalFrontends");
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
app.MapHub<WorkItemHub>("/hubs/work-items").RequireRateLimiting("realtime-connect");

var api = app.MapGroup("/api").RequireRateLimiting("api");
MapIdentity(api);
MapOrganizations(api);
MapTeams(api);
MapProjects(api);
MapBoards(api);
MapWorkflows(api);
MapWorkItems(api);
MapNotifications(api);
MapAudit(api);

app.MapGet("/", () => Results.Redirect("/swagger"));

app.Run();

static void MapIdentity(RouteGroupBuilder api)
{
    var group = api.MapGroup("/auth").WithTags("Identity");

    group.MapPost("/register", async (RegisterUserRequest request, IdentityService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.RegisterAsync(request, ct), http));

    group.MapPost("/login", async (LoginRequest request, IdentityService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.LoginAsync(request, ct), http))
        .RequireRateLimiting("login");

    group.MapPost("/refresh", async (RefreshTokenRequest request, IdentityService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.RefreshAsync(request, ct), http));

    group.MapPost("/logout", async (LogoutRequest request, IdentityService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.LogoutAsync(request, ct), http));

    group.MapPost("/change-password", async (ChangePasswordRequest request, IdentityService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.ChangePasswordAsync(request, ct), http))
        .RequireAuthorization();

    group.MapPost("/forgot-password", async (ForgotPasswordRequest request, IdentityService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.ForgotPasswordAsync(request, ct), http))
        .RequireRateLimiting("password-reset");

    group.MapPost("/reset-password", async (ResetPasswordRequest request, IdentityService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.ResetPasswordAsync(request, ct), http))
        .RequireRateLimiting("password-reset");

    group.MapGet("/mfa", async (IdentityService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.GetMfaStatusAsync(ct), http))
        .RequireAuthorization();

    group.MapPost("/mfa/setup", async (BeginMfaSetupRequest request, IdentityService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.BeginMfaSetupAsync(request, ct), http))
        .RequireAuthorization();

    group.MapPost("/mfa/confirm", async (ConfirmMfaSetupRequest request, IdentityService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.ConfirmMfaSetupAsync(request, ct), http))
        .RequireAuthorization();

    group.MapPost("/mfa/disable", async (DisableMfaRequest request, IdentityService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.DisableMfaAsync(request, ct), http))
        .RequireAuthorization();

    group.MapGet("/api-keys", async (ApiKeyService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.ListAsync(ct), http))
        .RequireAuthorization();

    group.MapPost("/api-keys", async (CreateApiKeyRequest request, ApiKeyService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.CreateAsync(request, CorrelationId(http), ct), http))
        .RequireAuthorization();

    group.MapDelete("/api-keys/{apiKeyId}", async (string apiKeyId, ApiKeyService service, HttpContext http, CancellationToken ct) =>
    {
        await service.RevokeAsync(apiKeyId, CorrelationId(http), ct);
        return Ok(new { revoked = true }, http);
    }).RequireAuthorization();

    group.MapGet("/privacy/export", async (PrivacyService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.ExportAsync(ct), http))
        .RequireAuthorization();

    group.MapPost("/privacy/anonymize", async (AnonymizeAccountRequest request, PrivacyService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.AnonymizeAsync(request, CorrelationId(http), ct), http))
        .RequireAuthorization()
        .RequireRateLimiting("password-reset");

    group.MapPost("/deactivate", async (DeactivateAccountRequest request, IdentityService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.DeactivateAsync(request, ct), http))
        .RequireAuthorization();

    group.MapGet("/users", async (string? search, IdentityService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.SearchUsersAsync(search, ct), http))
        .RequireAuthorization();

    group.MapGet("/roles", async (IdentityAdministrationService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.ListRolesAsync(ct), http))
        .RequireAuthorization();

    group.MapPost("/roles", async (CreateRoleRequest request, IdentityAdministrationService service, HttpContext http, CancellationToken ct) =>
        Created(await service.CreateRoleAsync(request, CorrelationId(http), ct), http))
        .RequireAuthorization();

    group.MapPut("/roles/{roleId}", async (string roleId, UpdateRoleRequest request, IdentityAdministrationService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.UpdateRoleAsync(roleId, request, CorrelationId(http), ct), http))
        .RequireAuthorization();

    group.MapDelete("/roles/{roleId}", async (string roleId, IdentityAdministrationService service, HttpContext http, CancellationToken ct) =>
    {
        await service.DeleteRoleAsync(roleId, CorrelationId(http), ct);
        return Ok(new { deleted = true }, http);
    }).RequireAuthorization();

    group.MapPut("/users/{userId}/roles", async (
        string userId,
        AssignUserRolesRequest request,
        IdentityAdministrationService service,
        HttpContext http,
        CancellationToken ct) =>
        Ok(await service.AssignRolesAsync(userId, request, CorrelationId(http), ct), http))
        .RequireAuthorization();
}

static void MapOrganizations(RouteGroupBuilder api)
{
    var group = api.MapGroup("/organizations").WithTags("Organizations").RequireAuthorization();

    group.MapGet("/", async (OrganizationService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.ListAsync(ct), http));

    group.MapPost("/", async (CreateOrganizationRequest request, OrganizationService service, HttpContext http, CancellationToken ct) =>
        Created(await service.CreateAsync(request, CorrelationId(http), ct), http));

    group.MapPut("/{organizationId}", async (string organizationId, UpdateOrganizationRequest request, OrganizationService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.UpdateAsync(organizationId, request, CorrelationId(http), ct), http));

    group.MapPost("/{organizationId}/departments", async (string organizationId, CreateDepartmentRequest request, OrganizationService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.CreateDepartmentAsync(organizationId, request, CorrelationId(http), ct), http));

    group.MapPut("/{organizationId}/departments/{departmentId}", async (string organizationId, string departmentId, UpdateDepartmentRequest request, OrganizationService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.UpdateDepartmentAsync(organizationId, departmentId, request, CorrelationId(http), ct), http));

    group.MapDelete("/{organizationId}/departments/{departmentId}", async (string organizationId, string departmentId, OrganizationService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.DeleteDepartmentAsync(organizationId, departmentId, CorrelationId(http), ct), http));

    group.MapPost("/{organizationId}/departments/{departmentId}/members", async (string organizationId, string departmentId, AssignDepartmentMemberRequest request, OrganizationService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.AssignMemberAsync(organizationId, departmentId, request, CorrelationId(http), ct), http));

    group.MapPatch("/{organizationId}/departments/{departmentId}/members/{userId}", async (
        string organizationId,
        string departmentId,
        string userId,
        UpdateDepartmentMemberRequest request,
        OrganizationService service,
        HttpContext http,
        CancellationToken ct) =>
        Ok(await service.UpdateMemberPositionAsync(organizationId, departmentId, userId, request, CorrelationId(http), ct), http));

    group.MapDelete("/{organizationId}/departments/{departmentId}/members/{userId}", async (
        string organizationId,
        string departmentId,
        string userId,
        OrganizationService service,
        HttpContext http,
        CancellationToken ct) =>
        Ok(await service.RemoveMemberAsync(organizationId, departmentId, userId, CorrelationId(http), ct), http));
}

static void MapTeams(RouteGroupBuilder api)
{
    var group = api.MapGroup("/teams").WithTags("Teams").RequireAuthorization();

    group.MapGet("/", async (string organizationId, bool? archived, TeamService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.ListAsync(organizationId, ct, archived ?? false), http));

    group.MapPost("/", async (CreateTeamRequest request, TeamService service, HttpContext http, CancellationToken ct) =>
        Created(await service.CreateAsync(request, CorrelationId(http), ct), http));

    group.MapPost("/{teamId}/members", async (string teamId, InviteTeamMemberRequest request, TeamService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.InviteAsync(teamId, request, CorrelationId(http), ct), http));

    group.MapPut("/{teamId}", async (string teamId, UpdateTeamRequest request, TeamService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.UpdateAsync(teamId, request, CorrelationId(http), ct), http));

    group.MapPost("/{teamId}/invites/{inviteId}/accept", async (string teamId, string inviteId, TeamService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.AcceptInviteAsync(teamId, inviteId, CorrelationId(http), ct), http));

    group.MapPost("/{teamId}/invites/{inviteId}/reject", async (string teamId, string inviteId, TeamService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.RejectInviteAsync(teamId, inviteId, CorrelationId(http), ct), http));

    group.MapPatch("/{teamId}/members/{userId}/role", async (
        string teamId,
        string userId,
        ChangeTeamMemberRoleRequest request,
        TeamService service,
        HttpContext http,
        CancellationToken ct) =>
        Ok(await service.ChangeMemberRoleAsync(teamId, userId, request, CorrelationId(http), ct), http));

    group.MapPost("/{teamId}/ownership-transfer", async (
        string teamId,
        TransferTeamOwnershipRequest request,
        TeamService service,
        HttpContext http,
        CancellationToken ct) =>
        Ok(await service.TransferOwnershipAsync(teamId, request, CorrelationId(http), ct), http));

    group.MapDelete("/{teamId}/members/{userIdOrEmail}", async (string teamId, string userIdOrEmail, TeamService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.RemoveMemberAsync(teamId, userIdOrEmail, CorrelationId(http), ct), http));

    group.MapDelete("/{teamId}", async (string teamId, TeamService service, HttpContext http, CancellationToken ct) =>
    {
        await service.ArchiveAsync(teamId, CorrelationId(http), ct);
        return Ok(new { archived = true }, http);
    });

    group.MapPost("/{teamId}/restore", async (string teamId, TeamService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.RestoreAsync(teamId, CorrelationId(http), ct), http));
}

static void MapProjects(RouteGroupBuilder api)
{
    var group = api.MapGroup("/projects").WithTags("Projects").RequireAuthorization();

    group.MapGet("/", async (string organizationId, bool? archived, ProjectService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.ListAsync(organizationId, ct, archived ?? false), http));

    group.MapPost("/", async (CreateProjectRequest request, ProjectService service, HttpContext http, CancellationToken ct) =>
        Created(await service.CreateAsync(request, CorrelationId(http), ct), http));

    group.MapPost("/{projectId}/members", async (string projectId, AddProjectMemberRequest request, ProjectService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.AddMemberAsync(projectId, request, CorrelationId(http), ct), http));

    group.MapPut("/{projectId}", async (string projectId, UpdateProjectRequest request, ProjectService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.UpdateAsync(projectId, request, CorrelationId(http), ct), http));

    group.MapPost("/{projectId}/teams", async (string projectId, AddProjectTeamRequest request, ProjectService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.AddTeamAsync(projectId, request, CorrelationId(http), ct), http));

    group.MapDelete("/{projectId}/teams/{teamId}", async (string projectId, string teamId, ProjectService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.RemoveTeamAsync(projectId, teamId, CorrelationId(http), ct), http));

    group.MapPatch("/{projectId}/members/{userId}/role", async (
        string projectId,
        string userId,
        ChangeProjectMemberRoleRequest request,
        ProjectService service,
        HttpContext http,
        CancellationToken ct) =>
        Ok(await service.ChangeMemberRoleAsync(projectId, userId, request, CorrelationId(http), ct), http));

    group.MapDelete("/{projectId}/members/{userId}", async (string projectId, string userId, ProjectService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.RemoveMemberAsync(projectId, userId, CorrelationId(http), ct), http));

    group.MapDelete("/{projectId}", async (string projectId, ProjectService service, HttpContext http, CancellationToken ct) =>
    {
        await service.ArchiveAsync(projectId, CorrelationId(http), ct);
        return Ok(new { archived = true }, http);
    });

    group.MapPost("/{projectId}/restore", async (string projectId, ProjectService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.RestoreAsync(projectId, CorrelationId(http), ct), http));
}

static void MapBoards(RouteGroupBuilder api)
{
    var group = api.MapGroup("/boards").WithTags("Boards").RequireAuthorization();

    group.MapGet("/by-project/{projectId}", async (string projectId, bool? archived, BoardService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.ListByProjectAsync(projectId, ct, archived ?? false), http));

    group.MapPost("/", async (CreateBoardRequest request, BoardService service, HttpContext http, CancellationToken ct) =>
        Created(await service.CreateAsync(request, CorrelationId(http), ct), http));

    group.MapPut("/{boardId}", async (string boardId, UpdateBoardRequest request, BoardService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.UpdateAsync(boardId, request, CorrelationId(http), ct), http));

    group.MapPatch("/{boardId}/swimlane", async (string boardId, UpdateSwimlaneRequest request, BoardService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.UpdateSwimlaneAsync(boardId, request, CorrelationId(http), ct), http));

    group.MapPost("/{boardId}/views", async (string boardId, CreateBoardViewRequest request, BoardService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.CreateViewAsync(boardId, request, CorrelationId(http), ct), http));

    group.MapPut("/{boardId}/views/{viewId}", async (string boardId, string viewId, UpdateBoardViewRequest request, BoardService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.UpdateViewAsync(boardId, viewId, request, CorrelationId(http), ct), http));

    group.MapDelete("/{boardId}/views/{viewId}", async (string boardId, string viewId, BoardService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.DeleteViewAsync(boardId, viewId, CorrelationId(http), ct), http));

    group.MapPost("/{boardId}/columns", async (string boardId, CreateColumnRequest request, BoardService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.AddColumnAsync(boardId, request, CorrelationId(http), ct), http));

    group.MapPut("/{boardId}/columns/{columnId}", async (
        string boardId,
        string columnId,
        UpdateColumnRequest request,
        BoardService service,
        HttpContext http,
        CancellationToken ct) =>
        Ok(await service.UpdateColumnAsync(boardId, columnId, request, CorrelationId(http), ct), http));

    group.MapPut("/{boardId}/columns/reorder", async (string boardId, ReorderColumnsRequest request, BoardService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.ReorderColumnsAsync(boardId, request, CorrelationId(http), ct), http));

    group.MapDelete("/{boardId}/columns/{columnId}", async (string boardId, string columnId, BoardService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.DeleteColumnAsync(boardId, columnId, CorrelationId(http), ct), http));

    group.MapDelete("/{boardId}", async (string boardId, BoardService service, HttpContext http, CancellationToken ct) =>
    {
        await service.ArchiveAsync(boardId, CorrelationId(http), ct);
        return Ok(new { archived = true }, http);
    });

    group.MapPost("/{boardId}/restore", async (string boardId, BoardService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.RestoreAsync(boardId, CorrelationId(http), ct), http));
}

static void MapWorkflows(RouteGroupBuilder api)
{
    var group = api.MapGroup("/workflows").WithTags("Workflows").RequireAuthorization();

    group.MapGet("/{projectId}", async (string projectId, WorkflowService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.GetOrCreateDefaultAsync(projectId, ct), http));

    group.MapPut("/{projectId}", async (string projectId, CreateWorkflowRequest request, WorkflowService service, HttpContext http, CancellationToken ct) =>
    {
        var normalized = request with { ProjectId = projectId };
        return Ok(await service.UpsertAsync(normalized, CorrelationId(http), ct), http);
    });
}

static void MapWorkItems(RouteGroupBuilder api)
{
    var group = api.MapGroup("/work-items").WithTags("WorkItems").RequireAuthorization();

    group.MapGet("/", async (
        string? projectId,
        string? assigneeUserId,
        string? status,
        string? text,
        int? page,
        int? pageSize,
        bool? archived,
        WorkItemService service,
        HttpContext http,
        CancellationToken ct) =>
        Ok(await service.SearchAsync(
            new WorkItemSearchRequest(projectId, assigneeUserId, status, text, page ?? 1, pageSize ?? 100, archived ?? false),
            ct), http))
        .RequireRateLimiting("search");

    group.MapGet("/{id}", async (string id, WorkItemService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.GetAsync(id, ct), http));

    group.MapPost("/", async (CreateWorkItemRequest request, WorkItemService service, HttpContext http, CancellationToken ct) =>
        Created(await service.CreateAsync(request, CorrelationId(http), ct), http));

    group.MapPost("/bulk/move", async (BulkMoveWorkItemsRequest request, WorkItemService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.BulkMoveAsync(request, CorrelationId(http), ct), http));

    group.MapPost("/bulk/assign", async (BulkAssignWorkItemsRequest request, WorkItemService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.BulkAssignAsync(request, CorrelationId(http), ct), http));

    group.MapPost("/bulk/archive", async (BulkArchiveWorkItemsRequest request, WorkItemService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.BulkArchiveAsync(request, CorrelationId(http), ct), http));

    group.MapPut("/{id}", async (string id, UpdateWorkItemRequest request, WorkItemService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.UpdateAsync(id, request, CorrelationId(http), ct), http));

    group.MapPatch("/{id}/assignee", async (string id, AssignWorkItemRequest request, WorkItemService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.AssignAsync(id, request, CorrelationId(http), ct), http));

    group.MapPatch("/{id}/status", async (string id, MoveWorkItemRequest request, WorkItemService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.MoveAsync(id, request, CorrelationId(http), ct), http));

    group.MapPatch("/{id}/rank", async (string id, ReorderWorkItemRequest request, WorkItemService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.ReorderAsync(id, request, CorrelationId(http), ct), http));

    group.MapPatch("/{id}/planning", async (string id, SetWorkItemPlanningRequest request, WorkItemService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.SetPlanningAsync(id, request, ct), http));

    group.MapPatch("/{id}/parent", async (string id, SetWorkItemParentRequest request, WorkItemService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.SetParentAsync(id, request, CorrelationId(http), ct), http));

    group.MapPatch("/{id}/team", async (string id, SetWorkItemTeamRequest request, WorkItemService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.SetTeamAsync(id, request, CorrelationId(http), ct), http));

    group.MapPost("/{id}/approvals", async (string id, RequestWorkItemApprovalRequest request, WorkItemService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.RequestApprovalAsync(id, request, CorrelationId(http), ct), http));

    group.MapPost("/{id}/approvals/{approvalId}/decision", async (
        string id,
        string approvalId,
        DecideWorkItemApprovalRequest request,
        WorkItemService service,
        HttpContext http,
        CancellationToken ct) =>
        Ok(await service.DecideApprovalAsync(id, approvalId, request, CorrelationId(http), ct), http));

    group.MapPost("/{id}/checklist", async (string id, AddChecklistItemRequest request, WorkItemService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.AddChecklistItemAsync(id, request, ct), http));

    group.MapPatch("/{id}/checklist/{itemId}", async (string id, string itemId, CompleteChecklistItemRequest request, WorkItemService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.CompleteChecklistItemAsync(id, itemId, request, ct), http));

    group.MapPost("/{id}/labels", async (string id, AddLabelRequest request, WorkItemService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.AddLabelAsync(id, request, ct), http));

    group.MapDelete("/{id}/labels/{label}", async (string id, string label, WorkItemService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.RemoveLabelAsync(id, label, ct), http));

    group.MapPost("/{id}/comments", async (string id, AddCommentRequest request, WorkItemService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.AddCommentAsync(id, request, CorrelationId(http), ct), http));

    group.MapPut("/{id}/comments/{commentId}", async (string id, string commentId, EditCommentRequest request, WorkItemService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.EditCommentAsync(id, commentId, request, CorrelationId(http), ct), http));

    group.MapDelete("/{id}/comments/{commentId}", async (string id, string commentId, WorkItemService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.DeleteCommentAsync(id, commentId, CorrelationId(http), ct), http));

    group.MapPost("/{id}/attachments/upload", async (string id, IFormFile file, WorkItemService service, HttpContext http, CancellationToken ct) =>
    {
        await using var stream = file.OpenReadStream();
        return Ok(await service.UploadAttachmentAsync(
            id,
            stream,
            file.FileName,
            file.ContentType,
            file.Length,
            CorrelationId(http),
            ct), http);
    })
    .DisableAntiforgery()
    .RequireRateLimiting("upload");

    group.MapGet("/{id}/attachments/{attachmentId}/download", async (
        string id,
        string attachmentId,
        WorkItemService service,
        CancellationToken ct) =>
    {
        var attachment = await service.OpenAttachmentAsync(id, attachmentId, ct);
        return Results.File(
            attachment.Content,
            attachment.ContentType,
            attachment.FileName,
            enableRangeProcessing: true);
    });

    group.MapGet("/{id}/attachments/{attachmentId}/preview", async (
        string id,
        string attachmentId,
        WorkItemService service,
        CancellationToken ct) =>
    {
        var attachment = await service.OpenAttachmentAsync(id, attachmentId, ct);
        if (!IsPreviewableContentType(attachment.ContentType))
        {
            await attachment.Content.DisposeAsync();
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        return Results.Stream(
            attachment.Content,
            attachment.ContentType,
            enableRangeProcessing: true);
    });

    group.MapDelete("/{id}/attachments/{attachmentId}", async (string id, string attachmentId, WorkItemService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.DeleteAttachmentAsync(id, attachmentId, CorrelationId(http), ct), http));

    group.MapPost("/{id}/worklogs", async (string id, AddWorkLogRequest request, WorkItemService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.AddWorkLogAsync(id, request, ct), http));

    group.MapPost("/{id}/relations", async (string id, LinkWorkItemRequest request, WorkItemService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.LinkAsync(id, request, CorrelationId(http), ct), http));

    group.MapDelete("/{id}/relations/{relatedWorkItemId}", async (
        string id,
        string relatedWorkItemId,
        string relationType,
        WorkItemService service,
        HttpContext http,
        CancellationToken ct) =>
        Ok(await service.UnlinkAsync(id, relatedWorkItemId, relationType, CorrelationId(http), ct), http));

    group.MapDelete("/{id}", async (string id, WorkItemService service, HttpContext http, CancellationToken ct) =>
    {
        await service.ArchiveAsync(id, CorrelationId(http), ct);
        return Ok(new { archived = true }, http);
    });

    group.MapPost("/{id}/restore", async (string id, WorkItemService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.RestoreAsync(id, CorrelationId(http), ct), http));

    group.MapGet("/reports/project-summary/{projectId}", async (string projectId, WorkItemService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.ProjectSummaryAsync(projectId, ct), http));

    group.MapGet("/reports/status-distribution/{projectId}", async (string projectId, WorkItemService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.StatusDistributionAsync(projectId, ct), http));

    group.MapGet("/reports/user-workload/{projectId}", async (string projectId, WorkItemService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.UserWorkloadAsync(projectId, ct), http));

    group.MapGet("/reports/due-date-risks/{projectId}", async (string projectId, int? days, WorkItemService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.DueDateRisksAsync(projectId, days ?? 14, ct), http));

    group.MapGet("/reports/sprint-burndown/{projectId}/{sprintId}", async (
        string projectId,
        string sprintId,
        DateOnly startDate,
        DateOnly endDate,
        WorkItemService service,
        HttpContext http,
        CancellationToken ct) =>
        Ok(await service.SprintBurndownAsync(projectId, sprintId, startDate, endDate, ct), http));

    group.MapGet("/reports/sprint-velocity/{projectId}", async (
        string projectId,
        int? sprintCount,
        WorkItemService service,
        HttpContext http,
        CancellationToken ct) =>
        Ok(await service.SprintVelocityAsync(projectId, sprintCount ?? 6, ct), http));

    group.MapGet("/reports/flow-time/{projectId}", async (
        string projectId,
        DateOnly? from,
        DateOnly? to,
        WorkItemService service,
        HttpContext http,
        CancellationToken ct) =>
        Ok(await service.FlowTimeAsync(projectId, from, to, ct), http));

    group.MapGet("/reports/completion-rate/{projectId}", async (
        string projectId,
        DateOnly? from,
        DateOnly? to,
        WorkItemService service,
        HttpContext http,
        CancellationToken ct) =>
        Ok(await service.CompletionRateAsync(projectId, from, to, ct), http));

    group.MapGet("/reports/team-performance/{projectId}", async (
        string projectId,
        DateOnly? from,
        DateOnly? to,
        WorkItemService service,
        HttpContext http,
        CancellationToken ct) =>
        Ok(await service.TeamPerformanceAsync(projectId, from, to, ct), http));
}

static void MapNotifications(RouteGroupBuilder api)
{
    var group = api.MapGroup("/notifications").WithTags("Notifications").RequireAuthorization();

    group.MapGet("/", async (
        int? page,
        int? pageSize,
        bool? unreadOnly,
        NotificationService service,
        ICurrentUser currentUser,
        HttpContext http,
        CancellationToken ct) =>
        Ok(await service.ListAsync(
            currentUser.UserId ?? throw new UnauthorizedException("Authenticated user is required."),
            ct,
            page ?? 1,
            pageSize ?? 50,
            unreadOnly ?? false), http));

    group.MapGet("/{userId}", async (
        string userId,
        int? page,
        int? pageSize,
        bool? unreadOnly,
        NotificationService service,
        HttpContext http,
        CancellationToken ct) =>
        Ok(await service.ListAsync(userId, ct, page ?? 1, pageSize ?? 50, unreadOnly ?? false), http));

    group.MapGet("/preferences/me", async (NotificationService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.GetPreferencesAsync(ct), http));

    group.MapPut("/preferences/me", async (
        UpdateNotificationPreferencesRequest request,
        NotificationService service,
        HttpContext http,
        CancellationToken ct) =>
        Ok(await service.UpdatePreferencesAsync(request, ct), http));

    group.MapPatch("/{notificationId}/read", async (string notificationId, NotificationService service, HttpContext http, CancellationToken ct) =>
    {
        await service.MarkAsReadAsync(notificationId, ct);
        return Ok(new { read = true }, http);
    });
}

static void MapAudit(RouteGroupBuilder api)
{
    var group = api.MapGroup("/audit").WithTags("Audit").RequireAuthorization();

    group.MapGet("/", async (
        string? actorUserId,
        string? action,
        string? entityType,
        string? entityId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int? page,
        int? pageSize,
        AuditService service,
        HttpContext http,
        CancellationToken ct) =>
        Ok(await service.QueryAsync(
            new AuditLogQuery(actorUserId, action, entityType, entityId, from, to, page ?? 1, pageSize ?? 50),
            ct),
            http));

    group.MapGet("/entity/{entityType}/{entityId}", async (string entityType, string entityId, AuditService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.ListByEntityAsync(entityType, entityId, ct), http));

    group.MapGet("/user/{actorUserId}", async (string actorUserId, AuditService service, HttpContext http, CancellationToken ct) =>
        Ok(await service.ListByUserAsync(actorUserId, ct), http));
}

static IResult Ok<T>(T data, HttpContext http) =>
    Results.Ok(ApiResponse<T>.Ok(data, CorrelationId(http)));

static IResult Created<T>(T data, HttpContext http) =>
    Results.Json(ApiResponse<T>.Ok(data, CorrelationId(http)), statusCode: StatusCodes.Status201Created);

static bool IsPreviewableContentType(string contentType) =>
    contentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase)
    || contentType.Equals("text/plain", StringComparison.OrdinalIgnoreCase)
    || contentType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase)
    || contentType.Equals("image/png", StringComparison.OrdinalIgnoreCase)
    || contentType.Equals("image/gif", StringComparison.OrdinalIgnoreCase)
    || contentType.Equals("image/webp", StringComparison.OrdinalIgnoreCase);

static string CorrelationId(HttpContext http)
{
    if (!http.Response.Headers.ContainsKey("X-Correlation-Id"))
    {
        http.Response.Headers["X-Correlation-Id"] = http.TraceIdentifier;
    }

    return http.TraceIdentifier;
}

public sealed class ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger, IHostEnvironment environment)
{
    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.Headers["X-Correlation-Id"] = context.TraceIdentifier;

        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            var (statusCode, code, message) = MapException(ex, environment.IsDevelopment());
            logger.LogError(ex, "Request failed with {Code}. CorrelationId: {CorrelationId}", code, context.TraceIdentifier);
            context.Response.StatusCode = (int)statusCode;
            await context.Response.WriteAsJsonAsync(ApiResponse<object>.Fail(code, message, context.TraceIdentifier));
        }
    }

    private static (HttpStatusCode StatusCode, string Code, string Message) MapException(Exception exception, bool includeDetails) =>
        exception switch
        {
            ValidationException ex => (HttpStatusCode.BadRequest, ex.Code, ex.Message),
            UnauthorizedException ex => (HttpStatusCode.Unauthorized, ex.Code, ex.Message),
            AuthenticationChallengeException ex => (HttpStatusCode.Unauthorized, ex.Code, ex.Message),
            ForbiddenException ex => (HttpStatusCode.Forbidden, ex.Code, ex.Message),
            NotFoundException ex => (HttpStatusCode.NotFound, ex.Code, ex.Message),
            ConflictException ex => (HttpStatusCode.Conflict, ex.Code, ex.Message),
            ZumboException ex => (HttpStatusCode.BadRequest, ex.Code, ex.Message),
            _ => (HttpStatusCode.InternalServerError, "UNEXPECTED_ERROR", includeDetails ? exception.Message : "Unexpected server error.")
        };
}

public sealed class ProjectPermissionCheckerAdapter(IDocumentRepository<ProjectDocument> projects) : IProjectPermissionChecker
{
    private static readonly IReadOnlyDictionary<string, string[]> RolePermissions =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["ProjectOwner"] =
            [
                "BoardManage", "WorkItemView", "WorkItemCreate", "WorkItemUpdate", "WorkItemAssign", "WorkItemMove",
                "WorkItemDelete", "WorkItemLink", "WorkItemApprove", "CommentCreate", "AttachmentCreate", "AttachmentDelete", "WorkLogCreate"
            ],
            ["ProjectAdmin"] =
            [
                "BoardManage", "WorkItemView", "WorkItemCreate", "WorkItemUpdate", "WorkItemAssign", "WorkItemMove",
                "WorkItemDelete", "WorkItemLink", "WorkItemApprove", "CommentCreate", "AttachmentCreate", "AttachmentDelete", "WorkLogCreate"
            ],
            ["Developer"] =
            [
                "WorkItemView", "WorkItemCreate", "WorkItemUpdate", "WorkItemAssign", "WorkItemMove", "WorkItemLink",
                "CommentCreate", "AttachmentCreate", "AttachmentDelete", "WorkLogCreate"
            ],
            ["Viewer"] = ["WorkItemView", "CommentCreate"]
        };

    public async Task EnsureCanAsync(string userId, string projectId, string permission, CancellationToken ct)
    {
        var project = await projects.SelectAsync(x => x.Id == projectId && !x.Archived, ct)
            ?? throw new NotFoundException("PROJECT_NOT_FOUND", "Project was not found.");
        var membership = project.Members.SingleOrDefault(x => x.UserId == userId)
            ?? throw new ForbiddenException("User is not a member of this project.");

        if (!RolePermissions.TryGetValue(membership.Role, out var permissions)
            || !permissions.Contains(permission, StringComparer.OrdinalIgnoreCase))
        {
            throw new ForbiddenException($"Project role '{membership.Role}' cannot perform '{permission}'.");
        }
    }
}

public sealed class ProjectMemberDirectoryAdapter(IUserRepository users) : IProjectMemberDirectory
{
    public async Task EnsureEligibleAsync(string userId, string organizationId, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(userId, ct)
            ?? throw new NotFoundException("USER_NOT_FOUND", "Project member user was not found.");
        if (!user.IsActive)
        {
            throw new ConflictException("USER_INACTIVE", "Inactive users cannot be added to projects.");
        }

        if (!string.Equals(user.OrganizationId, organizationId, StringComparison.Ordinal))
        {
            throw new ConflictException("PROJECT_MEMBER_ORGANIZATION_MISMATCH", "Project members must belong to the project organization.");
        }
    }
}

public sealed class ProjectTeamDirectoryAdapter(IDocumentRepository<TeamDocument> teams) : IProjectTeamDirectory
{
    public async Task<ProjectTeamDirectoryEntry?> FindAsync(string teamId, CancellationToken ct)
    {
        var team = await teams.SelectAsync(x => x.Id == teamId, ct);
        return team is null
            ? null
            : new ProjectTeamDirectoryEntry(team.Id, team.OrganizationId, !team.Archived);
    }
}

public sealed class ProjectTeamUsageCheckerAdapter(
    IDocumentRepository<WorkItemDocument> workItems) : IProjectTeamUsageChecker
{
    public async Task<bool> HasWorkItemsAsync(string projectId, string teamId, CancellationToken ct) =>
        await workItems.SelectAsync(x => x.ProjectId == projectId && x.TeamId == teamId, ct) is not null;
}

public sealed class OrganizationMemberDirectoryAdapter(IUserRepository users) : IOrganizationMemberDirectory
{
    public async Task EnsureEligibleAsync(string userId, string organizationId, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(userId, ct)
            ?? throw new NotFoundException("USER_NOT_FOUND", "Organization member user was not found.");
        if (!user.IsActive)
        {
            throw new ConflictException("USER_INACTIVE", "Inactive users cannot be assigned to departments.");
        }

        if (!string.Equals(user.OrganizationId, organizationId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException(
                "ORGANIZATION_MEMBER_TENANT_MISMATCH",
                "Department members must belong to the organization tenant.");
        }
    }
}

public sealed class BoardProjectAccessCheckerAdapter(
    IDocumentRepository<ProjectDocument> projects,
    ICurrentUser currentUser) : IBoardProjectAccessChecker
{
    public async Task EnsureCanAsync(string userId, string projectId, string permission, CancellationToken ct)
    {
        var project = await projects.SelectAsync(x => x.Id == projectId && !x.Archived, ct)
            ?? throw new NotFoundException("PROJECT_NOT_FOUND", "Project was not found.");
        if (currentUser.Roles.Any(x => x.Equals("SystemAdmin", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var membership = project.Members.SingleOrDefault(x => x.UserId == userId)
            ?? throw new ForbiddenException("User is not a member of this project.");
        var canManage = membership.Role is "ProjectOwner" or "ProjectAdmin";
        if (permission == "BoardView" || permission == "BoardManage" && canManage)
        {
            return;
        }

        throw new ForbiddenException($"Project role '{membership.Role}' cannot perform '{permission}'.");
    }
}

public sealed class WorkItemTeamPolicyAdapter(
    IDocumentRepository<ProjectDocument> projects,
    IDocumentRepository<TeamDocument> teams) : IWorkItemTeamPolicy
{
    public async Task EnsureCanAssignAsync(
        string projectId,
        string teamId,
        string? assigneeUserId,
        CancellationToken ct)
    {
        var project = await projects.SelectAsync(x => x.Id == projectId && !x.Archived, ct)
            ?? throw new NotFoundException("PROJECT_NOT_FOUND", "Project was not found.");
        if (!project.TeamIds.Contains(teamId))
        {
            throw new ConflictException("WORK_ITEM_TEAM_NOT_LINKED", "Team must be linked to the project.");
        }

        var team = await teams.SelectAsync(x => x.Id == teamId && !x.Archived, ct)
            ?? throw new NotFoundException("TEAM_NOT_FOUND", "Team was not found.");
        if (team.OrganizationId != project.OrganizationId)
        {
            throw new ConflictException("WORK_ITEM_TEAM_ORGANIZATION_MISMATCH", "Team must belong to the project organization.");
        }

        if (!string.IsNullOrWhiteSpace(assigneeUserId)
            && team.Members.All(x => x.UserId != assigneeUserId || x.Status != "Active"))
        {
            throw new ConflictException("WORK_ITEM_ASSIGNEE_NOT_IN_TEAM", "Assignee must be an active member of the work item team.");
        }
    }

    public async Task<IReadOnlyCollection<WorkItemTeamEntry>> ListProjectTeamsAsync(
        string projectId,
        CancellationToken ct)
    {
        var project = await projects.SelectAsync(x => x.Id == projectId && !x.Archived, ct)
            ?? throw new NotFoundException("PROJECT_NOT_FOUND", "Project was not found.");
        var teamIds = project.TeamIds.ToHashSet(StringComparer.Ordinal);
        var result = await teams.ListByFilterAsync(
            x => teamIds.Contains(x.Id) && !x.Archived,
            x => x.Name,
            pageSize: 100,
            cancellationToken: ct);
        return result.Select(x => new WorkItemTeamEntry(x.Id, x.Name)).ToList();
    }
}

public sealed class BoardPolicyAdapter(
    IDocumentRepository<BoardDocument> boards,
    IDocumentRepository<WorkItemDocument> workItems) : IBoardColumnUsageChecker, IBoardPlacementPolicy
{
    public async Task<BoardPlacement> ResolveInitialAsync(string projectId, string boardId, CancellationToken ct)
    {
        var board = await GetBoardAsync(projectId, boardId, ct);
        var column = board.Columns
            .OrderBy(x => x.Category == "Todo" ? 0 : 1)
            .ThenBy(x => x.Position)
            .FirstOrDefault()
            ?? throw new ConflictException("BOARD_REQUIRES_COLUMN", "Board must contain a column before creating work items.");
        return new BoardPlacement(column.Id, column.Name, column.WipLimit.HasValue);
    }

    public async Task<BoardPlacement> EnsureCanMoveAsync(
        string projectId,
        string boardId,
        string workItemId,
        string targetStatus,
        CancellationToken ct)
    {
        var board = await GetBoardAsync(projectId, boardId, ct);
        var column = board.Columns.SingleOrDefault(x => x.Name.Equals(targetStatus, StringComparison.OrdinalIgnoreCase))
            ?? throw new ConflictException("BOARD_STATUS_COLUMN_NOT_FOUND", "Target workflow status has no board column.");
        return new BoardPlacement(column.Id, column.Name, column.WipLimit.HasValue);
    }

    public async Task EnsureHasCapacityAsync(
        string boardId,
        string columnId,
        string? ignoredWorkItemId,
        CancellationToken ct)
    {
        var board = await boards.SelectAsync(x => x.Id == boardId && !x.Archived, ct)
            ?? throw new NotFoundException("BOARD_NOT_FOUND", "Board was not found.");
        var column = board.Columns.SingleOrDefault(x => x.Id == columnId)
            ?? throw new NotFoundException("BOARD_COLUMN_NOT_FOUND", "Board column was not found.");
        await EnsureCapacityCoreAsync(board.Id, column, ignoredWorkItemId, ct);
    }

    public async Task<bool> HasWorkItemsAsync(
        string boardId,
        string columnId,
        string columnName,
        CancellationToken ct) =>
        await workItems.SelectAsync(x =>
            x.BoardId == boardId
            && !x.Archived
            && (x.ColumnId == columnId || x.ColumnId == "" && x.Status == columnName), ct) is not null;

    public async Task<bool> HasBoardWorkItemsAsync(string boardId, CancellationToken ct) =>
        await workItems.SelectAsync(x => x.BoardId == boardId && !x.Archived, ct) is not null;

    private async Task<BoardDocument> GetBoardAsync(string projectId, string boardId, CancellationToken ct)
    {
        var board = await boards.SelectAsync(x => x.Id == boardId && !x.Archived, ct)
            ?? throw new NotFoundException("BOARD_NOT_FOUND", "Board was not found.");
        if (board.ProjectId != projectId)
        {
            throw new ConflictException("BOARD_PROJECT_MISMATCH", "Board does not belong to the requested project.");
        }

        return board;
    }

    private async Task EnsureCapacityCoreAsync(
        string boardId,
        BoardColumnDocument column,
        string? ignoredWorkItemId,
        CancellationToken ct)
    {
        if (column.WipLimit is null)
        {
            return;
        }

        var count = 0;
        var page = 1;
        while (count < column.WipLimit.Value)
        {
            var batch = await workItems.ListByFilterAsync(
                x => x.BoardId == boardId
                    && x.Id != ignoredWorkItemId
                    && !x.Archived
                    && (x.ColumnId == column.Id || x.ColumnId == "" && x.Status == column.Name),
                page: page,
                pageSize: 200,
                cancellationToken: ct);
            count += batch.Count;
            if (batch.Count < 200)
            {
                break;
            }

            page++;
        }

        if (count >= column.WipLimit.Value)
        {
            throw new ConflictException(
                "BOARD_WIP_LIMIT_EXCEEDED",
                $"Column '{column.Name}' has reached its WIP limit of {column.WipLimit.Value}.");
        }
    }
}

public sealed class TeamUserDirectoryAdapter(IUserRepository users) : ITeamUserDirectory
{
    public async Task<TeamUserDirectoryEntry?> FindByIdAsync(string userId, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(userId, ct);
        return user is null
            ? null
            : new TeamUserDirectoryEntry(user.Id, user.Email, user.OrganizationId, user.IsActive);
    }

    public async Task<TeamUserDirectoryEntry?> FindByEmailAsync(string email, CancellationToken ct)
    {
        var user = await users.GetByUsernameOrEmailAsync(email, ct);
        return user is null || !user.Email.Equals(email, StringComparison.OrdinalIgnoreCase)
            ? null
            : new TeamUserDirectoryEntry(user.Id, user.Email, user.OrganizationId, user.IsActive);
    }
}

public sealed class AuditAccessCheckerAdapter(
    IDocumentRepository<OrganizationDocument> organizations,
    IDocumentRepository<ProjectDocument> projects,
    IDocumentRepository<TeamDocument> teams,
    IDocumentRepository<BoardDocument> boards,
    IDocumentRepository<WorkItemDocument> workItems,
    IdentityPermissionService permissionService,
    ICurrentUser currentUser) : IAuditAccessChecker
{
    public async Task EnsureCanReadAsync(AuditLogQuery query, CancellationToken ct)
    {
        var userId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new UnauthorizedException("Authenticated user is required.");
        }

        var hasGlobalAuditAccess = await permissionService.HasPermissionAsync("AuditReadAll", ct);

        if (hasGlobalAuditAccess)
        {
            return;
        }

        if (query.EntityType is not null && query.EntityId is not null)
        {
            if (query.EntityType.Equals("Organization", StringComparison.OrdinalIgnoreCase))
            {
                var organization = await organizations.SelectAsync(x => x.Id == query.EntityId, ct)
                    ?? throw new NotFoundException("ORGANIZATION_NOT_FOUND", "Organization was not found.");
                if (!string.Equals(organization.OwnerUserId, userId, StringComparison.Ordinal))
                {
                    throw new ForbiddenException("User cannot read audit records for this organization.");
                }

                return;
            }

            if (query.EntityType.Equals("Team", StringComparison.OrdinalIgnoreCase))
            {
                var team = await teams.SelectAsync(x => x.Id == query.EntityId, ct)
                    ?? throw new NotFoundException("TEAM_NOT_FOUND", "Team was not found.");
                if (team.Members.All(x => x.UserId != userId || x.Status != "Active"))
                {
                    throw new ForbiddenException("User cannot read audit records for this team.");
                }

                return;
            }

            var projectId = await ResolveProjectIdAsync(query.EntityType, query.EntityId, ct);
            var project = await projects.SelectAsync(x => x.Id == projectId, ct)
                ?? throw new NotFoundException("PROJECT_NOT_FOUND", "Project was not found.");

            if (!hasGlobalAuditAccess && project.Members.All(x => x.UserId != userId))
            {
                throw new ForbiddenException("User cannot read audit records for this project.");
            }

            return;
        }

        if (string.Equals(query.ActorUserId, userId, StringComparison.Ordinal))
        {
            return;
        }

        throw new ForbiddenException("Audit queries must target the current user or an accessible project entity.");
    }

    private async Task<string> ResolveProjectIdAsync(string entityType, string entityId, CancellationToken ct)
    {
        if (entityType.Equals("WorkItem", StringComparison.OrdinalIgnoreCase))
        {
            var workItem = await workItems.SelectAsync(x => x.Id == entityId, ct)
                ?? throw new NotFoundException("WORK_ITEM_NOT_FOUND", "Work item was not found.");
            return workItem.ProjectId;
        }

        if (entityType.Equals("Board", StringComparison.OrdinalIgnoreCase))
        {
            var board = await boards.SelectAsync(x => x.Id == entityId, ct)
                ?? throw new NotFoundException("BOARD_NOT_FOUND", "Board was not found.");
            return board.ProjectId;
        }

        if (entityType.Equals("Project", StringComparison.OrdinalIgnoreCase))
        {
            return entityId;
        }

        throw new ValidationException("Audit entity type must be WorkItem, Board, Project, Team or Organization.");
    }
}

public sealed class WorkflowPolicyAdapter(WorkflowService workflows) : IWorkflowPolicy
{
    public async Task<WorkflowTransitionRule> EnsureTransitionAllowedAsync(
        string projectId,
        string fromStatus,
        string toStatus,
        CancellationToken ct)
    {
        var workflow = await workflows.GetOrCreateDefaultAsync(projectId, ct);
        var transition = workflow.Transitions.SingleOrDefault(x =>
            x.FromStatus.Equals(fromStatus, StringComparison.OrdinalIgnoreCase)
            && x.ToStatus.Equals(toStatus, StringComparison.OrdinalIgnoreCase));

        if (transition is null)
        {
            throw new ConflictException("WORKFLOW_TRANSITION_FORBIDDEN", $"Transition from {fromStatus} to {toStatus} is not allowed.");
        }

        return new WorkflowTransitionRule(
            transition.FromStatus,
            transition.ToStatus,
            transition.RequiresAssignee,
            transition.RequiresCompletedChecklist,
            transition.RequiresApproval,
            transition.Automations.Select(x => new WorkflowAutomationRule(x.Action, x.Value)).ToList(),
            workflow.Statuses.Single(x =>
                x.Name.Equals(transition.ToStatus, StringComparison.OrdinalIgnoreCase)).Category);
    }
}

public sealed class WorkflowProjectAccessCheckerAdapter(
    IDocumentRepository<ProjectDocument> projects,
    ICurrentUser currentUser) : IWorkflowProjectAccessChecker
{
    public Task EnsureCanViewAsync(string projectId, CancellationToken ct) =>
        EnsureAsync(projectId, manage: false, ct);

    public Task EnsureCanManageAsync(string projectId, CancellationToken ct) =>
        EnsureAsync(projectId, manage: true, ct);

    private async Task EnsureAsync(string projectId, bool manage, CancellationToken ct)
    {
        var userId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new UnauthorizedException("Authenticated user is required.");
        }

        var project = await projects.SelectAsync(x => x.Id == projectId && !x.Archived, ct)
            ?? throw new NotFoundException("PROJECT_NOT_FOUND", "Project was not found.");
        if (currentUser.Roles.Contains("SystemAdmin", StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        var membership = project.Members.SingleOrDefault(x => x.UserId == userId)
            ?? throw new ForbiddenException("User is not a member of this project.");
        if (manage && membership.Role is not ("ProjectOwner" or "ProjectAdmin"))
        {
            throw new ForbiddenException("Project owner or admin role is required to manage workflows.");
        }
    }
}

public partial class Program;
