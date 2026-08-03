using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.WorkItems;

public static class DevelopmentIntegrationLimits
{
    public const int MaximumConnectionsPerOrganization = 20;
    public const int MaximumMappingsPerConnection = 100;
    public const int MaximumLinksPerWorkItem = 50;
    public const int MaximumProviderRepositories = 100;
    public const int MaximumWorkItemReferencesPerEvent = 10;
    public const int MaximumWebhookPayloadBytes = 1_048_576;
    public const int DeliveryRetentionDays = 90;
    public const int ReplayWindowSeconds = 300;
}
