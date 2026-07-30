using System.Net;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Runtime;
using Zumbo.BuildingBlocks.Application.Search;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.BuildingBlocks.Infrastructure.Concurrency;
using Zumbo.BuildingBlocks.Infrastructure.Messaging;
using Zumbo.BuildingBlocks.Infrastructure.Runtime;
using Zumbo.BuildingBlocks.Infrastructure.Search;
using Zumbo.BuildingBlocks.Infrastructure.Security;
using Zumbo.BuildingBlocks.Infrastructure.Storage;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Notifications;
using Zumbo.Modules.WorkItems;
using Zumbo.Persistence.PostgreSql;
using Zumbo.SharedKernel;
using MongoDurableTransactionRunner = Zumbo.BuildingBlocks.Infrastructure.Persistence.MongoDurableTransactionRunner;
using MongoTransactionContext = Zumbo.BuildingBlocks.Infrastructure.Persistence.MongoTransactionContext;

internal static class ApiHostRegistration
{
    internal static WebApplicationBuilder AddZumboHost(this WebApplicationBuilder builder)
    {
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

        ValidateRegistrationProvisioning(builder);
        ApiConfigurationValidation.Validate(builder);

        var requestLimits = builder.Configuration.GetSection("RequestLimits").Get<RequestLimitsOptions>()
            ?? new RequestLimitsOptions();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = requestLimits.MaxRequestBodyBytes;
            options.Limits.MaxRequestHeaderCount = requestLimits.MaxHeaderCount;
            options.Limits.MaxRequestHeadersTotalSize = requestLimits.MaxHeaderBytes;
        });

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IExpectedVersionAccessor, HttpExpectedVersionAccessor>();
        builder.Services.Configure<FormOptions>(options =>
        {
            options.MultipartBodyLengthLimit = requestLimits.MaxRequestBodyBytes;
            options.ValueLengthLimit = 16 * 1024;
            options.MultipartHeadersLengthLimit = 16 * 1024;
        });
        builder.Services.Configure<RequestLimitsOptions>(builder.Configuration.GetSection("RequestLimits"));
        builder.Services.AddZumboExternalDependencyResilience(builder.Configuration);
        builder.AddZumboObservability();
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
                    .AllowAnyMethod()
                    .WithExposedHeaders(
                        "X-Zumbo-Report-Generated-At",
                        "X-Zumbo-Report-Source-Version",
                        "X-Zumbo-Report-Stale",
                        "X-Zumbo-Report-Age-Seconds",
                        "X-Zumbo-Export-Format")
                    .AllowCredentials());
        });
        var rateLimits = builder.Configuration.GetSection("RateLimiting").Get<RateLimitingOptions>() ?? new RateLimitingOptions();
        builder.Services.Configure<RateLimitingOptions>(builder.Configuration.GetSection("RateLimiting"));
        builder.Services.AddZumboDistributedLocking(builder.Configuration);
        if (rateLimits.Provider.Equals("Redis", StringComparison.OrdinalIgnoreCase))
        {
            builder.Services.AddSingleton<IDistributedRateLimitCounter, RedisRateLimitCounter>();
        }
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
                        Window = TimeSpan.FromSeconds(rateLimits.StandardWindowSeconds),
                        QueueLimit = 0
                    }));
            options.AddPolicy("password-reset", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = Math.Clamp(rateLimits.PasswordResetPermitLimit, 1, 100),
                        Window = TimeSpan.FromSeconds(rateLimits.PasswordResetWindowSeconds),
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
                        Window = TimeSpan.FromSeconds(rateLimits.StandardWindowSeconds),
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
                        Window = TimeSpan.FromSeconds(rateLimits.StandardWindowSeconds),
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
                        Window = TimeSpan.FromSeconds(rateLimits.StandardWindowSeconds),
                        QueueLimit = 0
                    }));
            options.AddPolicy("intake-public", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = Math.Clamp(rateLimits.IntakePublicPermitLimit, 1, 1_000),
                        Window = TimeSpan.FromSeconds(rateLimits.StandardWindowSeconds),
                        QueueLimit = 0
                    }));
            options.AddPolicy("report", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                        ?? context.Connection.RemoteIpAddress?.ToString()
                        ?? "anonymous",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = Math.Clamp(rateLimits.ReportPermitLimit, 1, 5_000),
                        Window = TimeSpan.FromSeconds(rateLimits.StandardWindowSeconds),
                        QueueLimit = 0
                    }));
            options.AddPolicy("bulk", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                        ?? context.Connection.RemoteIpAddress?.ToString()
                        ?? "anonymous",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = Math.Clamp(rateLimits.BulkPermitLimit, 1, 1_000),
                        Window = TimeSpan.FromSeconds(rateLimits.StandardWindowSeconds),
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
                        Window = TimeSpan.FromSeconds(rateLimits.StandardWindowSeconds),
                        QueueLimit = 0
                    }));
        });

        builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
        builder.Services.Configure<BrowserSessionOptions>(builder.Configuration.GetSection("BrowserSession"));
        builder.Services.Configure<LoginSecurityOptions>(builder.Configuration.GetSection("LoginSecurity"));
        builder.Services.Configure<IdentityBootstrapOptions>(builder.Configuration.GetSection("IdentityBootstrap"));
        builder.Services.Configure<RegistrationProvisioningOptions>(builder.Configuration.GetSection("RegistrationProvisioning"));
        builder.Services.Configure<PasswordResetOptions>(builder.Configuration.GetSection("PasswordReset"));
        builder.Services.Configure<EmailNotificationOptions>(builder.Configuration.GetSection("Notifications:Email"));
        builder.Services.Configure<DueDateReminderOptions>(builder.Configuration.GetSection("Notifications:DueDateReminder"));
        builder.Services.Configure<WorkItemReadModelCacheOptions>(builder.Configuration.GetSection("ReadModelCache"));
        builder.Services.Configure<Zumbo.BuildingBlocks.Infrastructure.Persistence.MongoDbSettings>(builder.Configuration.GetSection("MongoDb"));
        builder.Services.Configure<Zumbo.BuildingBlocks.Infrastructure.Persistence.PersistenceOptions>(builder.Configuration.GetSection("Persistence"));
        builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection("Storage"));
        builder.Services.Configure<LocalStorageOptions>(builder.Configuration.GetSection("Storage:Local"));
        builder.Services.Configure<MinioStorageOptions>(builder.Configuration.GetSection("Storage:Minio"));
        builder.Services.AddOptions<AttachmentSecurityOptions>()
            .Bind(builder.Configuration.GetSection("AttachmentSecurity"))
            .Validate(
                AttachmentSecurityConfiguration.IsValid,
                "AttachmentSecurity contains an unsupported scanner or an out-of-range security limit.")
            .ValidateOnStart();
        builder.Services.Configure<SearchOptions>(builder.Configuration.GetSection("Search"));
        builder.Services.Configure<OpenSearchOptions>(builder.Configuration.GetSection("Search:OpenSearch"));

        var jwtOptions = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
        var jwtSigningKeys = jwtOptions.ResolveSigningKeys();
        _ = jwtOptions.ResolveActiveSigningKey();
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
                    IssuerSigningKeyResolver = (_, _, keyId, _) =>
                    {
                        if (!string.IsNullOrWhiteSpace(keyId)
                            && jwtSigningKeys.TryGetValue(keyId, out var signingKey))
                        {
                            return
                            [
                                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey))
                                {
                                    KeyId = keyId
                                }
                            ];
                        }

                        if (jwtOptions.SigningKeys.Count == 0 && jwtSigningKeys.Count == 1)
                        {
                            var legacy = jwtSigningKeys.Single();
                            return
                            [
                                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(legacy.Value))
                                {
                                    KeyId = legacy.Key
                                }
                            ];
                        }

                        return [];
                    },
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
                        else if (!context.Request.Headers.ContainsKey("Authorization"))
                        {
                            var browserOptions = context.HttpContext.RequestServices
                                .GetRequiredService<IOptions<BrowserSessionOptions>>().Value;
                            context.Token = context.Request.Cookies[browserOptions.AccessCookieName];
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
                        var sessionStore = context.HttpContext.RequestServices.GetRequiredService<IRefreshSessionStore>();
                        var clock = context.HttpContext.RequestServices.GetRequiredService<IClock>();
                        var user = await repository.GetByIdAsync(userId, context.HttpContext.RequestAborted);
                        var storedSession = user is null
                            ? null
                            : await sessionStore.GetByIdAsync(
                                sessionId,
                                user.Id,
                                user.OrganizationId,
                                context.HttpContext.RequestAborted);
                        var sessionIsActive = storedSession is not null
                            ? storedSession.IsActive(clock.UtcNow)
                            : user?.RefreshTokens.Any(x =>
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

        var realtimeOptions = builder.Configuration.GetSection("Realtime").Get<WorkItemRealtimeOptions>()
            ?? new WorkItemRealtimeOptions();
        realtimeOptions.Validate();
        builder.Services.AddOptions<WorkItemRealtimeOptions>()
            .BindConfiguration("Realtime")
            .Validate(options =>
            {
                try
                {
                    options.Validate();
                    return true;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            }, "Realtime limits are invalid.")
            .ValidateOnStart();
        var signalR = builder.Services.AddSignalR(options =>
        {
            options.EnableDetailedErrors = builder.Environment.IsDevelopment();
            options.MaximumReceiveMessageSize = realtimeOptions.MaximumPayloadBytes;
            options.MaximumParallelInvocationsPerClient = 1;
            options.StreamBufferCapacity = 10;
            options.ClientTimeoutInterval = TimeSpan.FromSeconds(realtimeOptions.ClientTimeoutSeconds);
            options.KeepAliveInterval = TimeSpan.FromSeconds(realtimeOptions.KeepAliveSeconds);
            options.StatefulReconnectBufferSize = realtimeOptions.StatefulReconnectBufferBytes;
        });
        if (builder.Configuration.GetValue<string>("Realtime:Backplane")
                ?.Equals("Redis", StringComparison.OrdinalIgnoreCase) == true)
        {
            var realtimeRedis = builder.Configuration["Realtime:Redis:ConnectionString"]
                ?? builder.Configuration["DistributedLock:Redis:ConnectionString"];
            if (string.IsNullOrWhiteSpace(realtimeRedis))
            {
                throw new InvalidOperationException("Realtime:Redis:ConnectionString must be configured when the Redis backplane is selected.");
            }
            signalR.AddStackExchangeRedis(realtimeRedis, options =>
            {
                options.Configuration.ChannelPrefix = StackExchange.Redis.RedisChannel.Literal("zumbo:realtime");
            });
        }

        builder.Services.AddSingleton<IClock, Zumbo.BuildingBlocks.Infrastructure.Runtime.SystemClock>();
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

        var storageProvider = StorageConfiguration.GetValidatedProvider(builder.Configuration);
        if (storageProvider.Equals("Minio", StringComparison.OrdinalIgnoreCase))
        {
            builder.Services.AddSingleton<IFileStorage, MinioFileStorage>();
        }
        else if (storageProvider.Equals("Local", StringComparison.OrdinalIgnoreCase))
        {
            builder.Services.AddSingleton<IFileStorage, LocalFileStorage>();
        }

        var scannerProvider = builder.Configuration.GetValue<string>("AttachmentSecurity:ScannerProvider") ?? "PolicyOnly";
        if (scannerProvider.Equals("ClamAv", StringComparison.Ordinal))
        {
            builder.Services.AddSingleton<IAttachmentMalwareScanner, ClamAvAttachmentMalwareScanner>();
        }
        else
        {
            builder.Services.AddSingleton<IAttachmentMalwareScanner, PolicyOnlyAttachmentMalwareScanner>();
        }

        var runtimeRole = builder.Configuration.GetValue<string>("Runtime:Role") ?? "Api";
        var isWorkerRole = runtimeRole.Equals("Worker", StringComparison.OrdinalIgnoreCase);
        var searchProvider = builder.Configuration.GetValue<string>("Search:Provider") ?? "InMemory";
        if (searchProvider.Equals("OpenSearch", StringComparison.OrdinalIgnoreCase))
        {
            builder.Services.AddHttpClient("WorkItemOpenSearch", client =>
                client.Timeout = Timeout.InfiniteTimeSpan);
            builder.Services.AddSingleton<IWorkItemSearchIndex>(provider =>
                new OpenSearchWorkItemSearchIndex(
                    provider.GetRequiredService<IHttpClientFactory>().CreateClient("WorkItemOpenSearch"),
                    provider.GetRequiredService<IOptions<OpenSearchOptions>>(),
                    provider.GetRequiredService<IExternalDependencyPolicyProvider>()));
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
        builder.Services.AddSingleton<IDurableMessageJitter, RandomDurableMessageJitter>();
        if (provider.Equals("Mongo", StringComparison.OrdinalIgnoreCase))
        {
            builder.Services.Configure<MongoMigrationOptions>(builder.Configuration.GetSection("MongoMigrations"));
            builder.Services.AddSingleton<MongoMigrationRunner>();
            builder.Services.AddSingleton<
                Zumbo.BuildingBlocks.Infrastructure.Persistence.IMongoDbService,
                Zumbo.BuildingBlocks.Infrastructure.Persistence.MongoDbService>();
            builder.Services.AddScoped<MongoTransactionContext>();
            builder.Services.AddScoped<IDurableTransactionRunner, MongoDurableTransactionRunner>();
            builder.Services.AddScoped<IDurableEventOutbox, MongoDurableEventOutbox>();
            builder.Services.AddScoped<IDurableEventInbox, MongoDurableEventInbox>();
            builder.Services.AddScoped(
                typeof(IDocumentRepository<>),
                typeof(Zumbo.BuildingBlocks.Infrastructure.Persistence.MongoRepository<>));
            if (!isWorkerRole)
            {
                builder.Services.AddHostedService<MongoIndexInitializer>();
            }
        }
        else if (provider.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase))
        {
            builder.Services.AddZumboPostgreSql(options =>
            {
                options.ConnectionString = builder.Configuration["PostgreSql:ConnectionString"]
                    ?? builder.Configuration.GetConnectionString("PostgreSql")
                    ?? string.Empty;
                options.CommandTimeoutSeconds = builder.Configuration.GetValue("PostgreSql:CommandTimeoutSeconds", 30);
                options.ConnectionTimeoutSeconds = builder.Configuration.GetValue("PostgreSql:ConnectionTimeoutSeconds", 5);
                options.MinimumPoolSize = builder.Configuration.GetValue("PostgreSql:MinimumPoolSize", 0);
                options.MaximumPoolSize = builder.Configuration.GetValue("PostgreSql:MaximumPoolSize", 100);
                options.MapDocument<Zumbo.Modules.Identity.UserDocument>("identity", "users");
                options.MapDocument<Zumbo.Modules.Identity.RefreshSessionDocument>("identity", "refresh_sessions");
                options.MapDocument<Zumbo.Modules.Identity.ApiKeyDocument>("identity", "api_keys");
                options.MapDocument<Zumbo.Modules.Identity.IdentityRoleDocument>("identity", "identity_roles");
                options.MapDocument<Zumbo.Modules.Identity.PrivacyWorkflowDocument>("identity", "privacy_workflows");
                options.MapDocument<Zumbo.Modules.Organizations.OrganizationDocument>("organizations", "organizations");
                options.MapDocument<Zumbo.Modules.Teams.TeamDocument>("teams", "teams");
                options.MapDocument<Zumbo.Modules.Projects.ProjectDocument>("projects", "projects");
                options.MapDocument<Zumbo.Modules.Projects.PortfolioDocument>("projects", "portfolios");
                options.MapDocument<Zumbo.Modules.Projects.GoalDocument>("projects", "goals");
                options.MapDocument<Zumbo.Modules.Projects.KnowledgeDocument>("projects", "knowledge_documents");
                options.MapDocument<Zumbo.Modules.Boards.BoardDocument>("boards", "boards");
                options.MapDocument<Zumbo.Modules.WorkItems.WorkItemDocument>("work_items", "work_items");
                options.MapDocument<Zumbo.Modules.WorkItems.WorkItemTypeSchemaDocument>("work_items", "work_item_type_schemas");
                options.MapDocument<Zumbo.Modules.WorkItems.WorkItemRelationEdgeDocument>("work_items", "work_item_relation_edges");
                options.MapDocument<Zumbo.Modules.WorkItems.WorkItemCollaborationDocument>("work_items", "work_item_collaborations");
                options.MapDocument<Zumbo.Modules.WorkItems.WorkItemEventActivityDocument>("work_items", "work_item_event_activities");
                options.MapDocument<Zumbo.Modules.WorkItems.WorkItemTemplateDocument>("work_items", "work_item_templates");
                options.MapDocument<Zumbo.Modules.WorkItems.WorkItemRecurrenceDocument>("work_items", "work_item_recurrences");
                options.MapDocument<Zumbo.Modules.WorkItems.WorkItemRecurrenceOccurrenceDocument>("work_items", "work_item_recurrence_occurrences");
                options.MapDocument<Zumbo.Modules.WorkItems.WorkItemBulkJobDocument>("work_items", "work_item_bulk_jobs");
                options.MapDocument<Zumbo.Modules.WorkItems.WorkItemBulkJobItemDocument>("work_items", "work_item_bulk_job_items");
                options.MapDocument<Zumbo.Modules.WorkItems.IntakeFormDocument>("work_items", "intake_forms");
                options.MapDocument<Zumbo.Modules.WorkItems.IntakeFormVersionDocument>("work_items", "intake_form_versions");
                options.MapDocument<Zumbo.Modules.WorkItems.IntakeSubmissionDocument>("work_items", "intake_submissions");
                options.MapDocument<Zumbo.Modules.WorkItems.DashboardDocument>("work_items", "dashboards");
                options.MapDocument<Zumbo.Modules.WorkItems.CapacityPlanDocument>("work_items", "capacity_plans");
                options.MapDocument<Zumbo.Modules.WorkItems.DevelopmentConnectionDocument>("work_items", "development_connections");
                options.MapDocument<Zumbo.Modules.WorkItems.DevelopmentRepositoryMappingDocument>("work_items", "development_repository_mappings");
                options.MapDocument<Zumbo.Modules.WorkItems.WorkItemDevelopmentLinkDocument>("work_items", "work_item_development_links");
                options.MapDocument<Zumbo.Modules.WorkItems.DevelopmentWebhookReceiptDocument>("work_items", "development_webhook_receipts");
                options.MapDocument<Zumbo.Modules.WorkItems.WebhookSubscriptionDocument>("work_items", "webhook_subscriptions");
                options.MapDocument<Zumbo.Modules.WorkItems.WebhookDeliveryDocument>("work_items", "webhook_deliveries");
                options.MapDocument<Zumbo.Modules.WorkItems.BoardColumnWipProjectionDocument>("work_items", "board_column_wip_projections");
                options.MapDocument<Zumbo.Modules.WorkItems.SprintDocument>("work_items", "sprints");
                options.MapDocument<Zumbo.Modules.WorkItems.SprintScopeSnapshotDocument>("work_items", "sprint_scope_snapshots");
                options.MapDocument<Zumbo.Modules.WorkItems.SprintCompletionSnapshotDocument>("work_items", "sprint_completion_snapshots");
                options.MapDocument<Zumbo.Modules.WorkItems.WorkItemCommentActivityDocument>("work_items", "work_item_comments");
                options.MapDocument<Zumbo.Modules.WorkItems.WorkItemCommentRevisionActivityDocument>("work_items", "work_item_comment_revisions");
                options.MapDocument<Zumbo.Modules.WorkItems.WorkItemAttachmentActivityDocument>("work_items", "work_item_attachments");
                options.MapDocument<Zumbo.Modules.WorkItems.WorkItemWorkLogActivityDocument>("work_items", "work_item_work_logs");
                options.MapDocument<Zumbo.Modules.WorkItems.WorkItemApprovalActivityDocument>("work_items", "work_item_approvals");
                options.MapDocument<Zumbo.Modules.WorkItems.WorkItemTimelineActivityDocument>("work_items", "work_item_timeline");
                options.MapDocument<Zumbo.Modules.Workflows.WorkflowDefinitionDocument>("workflows", "workflow_definitions");
                options.MapDocument<Zumbo.Modules.Workflows.AutomationRuleDocument>("workflows", "automation_rules");
                options.MapDocument<Zumbo.Modules.Workflows.AutomationRunDocument>("workflows", "automation_runs");
                options.MapDocument<Zumbo.Modules.Notifications.NotificationDocument>("notifications", "notifications");
                options.MapDocument<Zumbo.Modules.Notifications.NotificationPreferenceDocument>("notifications", "notification_preferences");
                options.MapDocument<Zumbo.Modules.Audit.AuditLogDocument>("audit", "audit_logs");
            });
        }
        else if (provider.Equals("InMemory", StringComparison.OrdinalIgnoreCase))
        {
            builder.Services.AddSingleton(
                typeof(IDocumentRepository<>),
                typeof(Zumbo.BuildingBlocks.Infrastructure.Persistence.InMemoryDocumentRepository<>));
            builder.Services.AddSingleton<IDurableTransactionRunner, InMemoryDurableTransactionRunner>();
            builder.Services.AddSingleton<IDurableEventOutbox, InMemoryDurableEventOutbox>();
            builder.Services.AddSingleton<IDurableEventInbox, InMemoryDurableEventInbox>();
        }
        else
        {
            throw new InvalidOperationException(
                $"Unsupported persistence provider '{provider}'. Expected InMemory, Mongo, or PostgreSql.");
        }

        builder.Services.Configure<DurableEventProcessorOptions>(
            builder.Configuration.GetSection("DurableMessaging"));
        builder.Services.AddScoped<DurableEventProcessor>();
        if (isWorkerRole || builder.Configuration.GetValue("BackgroundJobs:Enabled", true))
        {
            builder.Services.AddHostedService<DurableEventWorker>();
            builder.Services.AddHostedService<AttachmentSecurityMaintenanceHostedService>();
        }

        var dependencyHealthTimeout = TimeSpan.FromSeconds(5);
        var healthChecks = builder.Services.AddHealthChecks()
            .AddCheck<StorageHealthCheck>("storage", timeout: dependencyHealthTimeout, tags: ["ready"])
            .AddCheck<ExternalDependencyPolicyHealthCheck>(
                "external_dependency_policies",
                timeout: dependencyHealthTimeout,
                tags: ["ready"])
            .AddCheck<DurableMessagingHealthCheck>(
                "durable_messaging",
                timeout: dependencyHealthTimeout,
                tags: ["ready"]);
        if (provider.Equals("Mongo", StringComparison.OrdinalIgnoreCase))
        {
            healthChecks.AddCheck<MongoHealthCheck>("mongodb", timeout: dependencyHealthTimeout, tags: ["ready"]);
        }
        else if (provider.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase))
        {
            healthChecks.AddCheck<PostgreSqlHealthCheck>("postgresql", timeout: dependencyHealthTimeout, tags: ["ready"]);
        }

        if ((builder.Configuration.GetValue<string>("DistributedLock:Provider") ?? "InMemory")
            .Equals("Redis", StringComparison.OrdinalIgnoreCase))
        {
            healthChecks.AddCheck<RedisHealthCheck>("redis", timeout: dependencyHealthTimeout, tags: ["ready"]);
        }
        return builder;
    }

    private static void ValidateRegistrationProvisioning(WebApplicationBuilder builder)
    {
        var mode = builder.Configuration["RegistrationProvisioning:Mode"]
            ?? RegistrationProvisioningModes.ProductionLike;
        if (mode.Equals(RegistrationProvisioningModes.LocalDemo, StringComparison.OrdinalIgnoreCase))
        {
            if (!builder.Environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    "RegistrationProvisioning:Mode=LocalDemo is allowed only in Development.");
            }

            return;
        }

        if (!mode.Equals(RegistrationProvisioningModes.ProductionLike, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "RegistrationProvisioning:Mode must be ProductionLike or LocalDemo.");
        }
    }
}
