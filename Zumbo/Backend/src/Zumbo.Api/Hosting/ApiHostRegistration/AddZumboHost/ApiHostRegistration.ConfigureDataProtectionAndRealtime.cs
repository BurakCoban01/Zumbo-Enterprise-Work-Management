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

internal static partial class ApiHostRegistration
{
private static void ConfigureDataProtectionAndRealtime(WebApplicationBuilder builder)
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

}}
