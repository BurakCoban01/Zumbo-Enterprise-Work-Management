using Zumbo.Modules.WorkItems;
using Zumbo.Modules.WorkItems.Application.Features.Development.Connections;
using Zumbo.Modules.WorkItems.Application.Features.Development.Credentials;
using Zumbo.Modules.WorkItems.Application.Features.Development.Links;
using Zumbo.Modules.WorkItems.Application.Features.Development.Mappings;
using Zumbo.Modules.WorkItems.Application.Features.Development.ProviderHealth;
using Zumbo.Modules.WorkItems.Application.Features.Development.Repositories;
using Zumbo.Modules.WorkItems.Application.Features.Development.Webhooks;

namespace Zumbo.Api.Composition.Modules.WorkItems;

internal static class DevelopmentIntegrationComposition
{
    internal static IServiceCollection AddDevelopmentIntegrationServices(this IServiceCollection services)
    {
        services.AddOptions<DevelopmentProviderOptions>()
            .BindConfiguration("DevelopmentProviders")
            .Validate(
                options => options.RequestTimeoutSeconds is >= 1 and <= 30
                    && options.MaximumResponseBytes is >= 1_024 and <= 8 * 1_024 * 1_024
                    && options.AllowedHosts.Length is >= 1 and <= 100
                    && options.AllowedHosts.All(host =>
                        !string.IsNullOrWhiteSpace(host)
                        && host.Length <= 253
                        && !host.Contains('*')),
                "Development provider configuration is invalid.")
            .ValidateOnStart();
        services.AddSingleton<DevelopmentProviderTargetPolicy>();
        services.AddSingleton<IDevelopmentProviderGateway, DevelopmentProviderGateway>();
        services.AddSingleton<IDevelopmentCredentialProtector, DevelopmentCredentialProtectorAdapter>();
        services.AddScoped<IDevelopmentIntegrationAuthorization, DevelopmentIntegrationAuthorizationAdapter>();
        services.AddScoped<IDevelopmentProjectDirectory, DevelopmentProjectDirectoryAdapter>();
        services.AddScoped<DevelopmentIntegrationService>();
        services.AddScoped<CreateConnectionHandler>();
        services.AddScoped<ListConnectionsHandler>();
        services.AddScoped<GetConnectionHandler>();
        services.AddScoped<RotateCredentialHandler>();
        services.AddScoped<RotateWebhookSecretHandler>();
        services.AddScoped<DisconnectConnectionHandler>();
        services.AddScoped<DeleteConnectionHandler>();
        services.AddScoped<ListConnectionMappingsHandler>();
        services.AddScoped<CreateMappingHandler>();
        services.AddScoped<DeleteMappingHandler>();
        services.AddScoped<ListWorkItemMappingsHandler>();
        services.AddScoped<ListWorkItemLinksHandler>();
        services.AddScoped<CreateWorkItemLinkHandler>();
        services.AddScoped<DeleteWorkItemLinkHandler>();
        services.AddScoped<ReceiveWebhookHandler>();
        services.AddScoped<ApplyWebhookLinksHandler>();
        services.AddScoped<ProcessWebhookHandler>();
        services.AddScoped<CheckProviderHealthHandler>();
        services.AddScoped<ListRepositoriesHandler>();
        services.AddScoped<DevelopmentWebhookReceiptRetentionService>();
        return services;
    }
}
