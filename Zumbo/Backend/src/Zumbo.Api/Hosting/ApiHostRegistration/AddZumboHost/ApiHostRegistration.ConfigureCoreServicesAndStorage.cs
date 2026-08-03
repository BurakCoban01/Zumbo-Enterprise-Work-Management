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
private static void ConfigureCoreServicesAndStorage(WebApplicationBuilder builder)
{

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

}}
