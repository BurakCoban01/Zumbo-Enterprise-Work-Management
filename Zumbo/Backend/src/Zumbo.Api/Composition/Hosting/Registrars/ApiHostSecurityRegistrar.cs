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

namespace Zumbo.Api.Composition.Hosting.Registrars;

internal static class ApiHostSecurityRegistrar
{
    internal static void ConfigureAuthentication(WebApplicationBuilder builder)
{

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

}
    internal static void ConfigureDataProtectionAndRealtime(WebApplicationBuilder builder)
{

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

}
}
