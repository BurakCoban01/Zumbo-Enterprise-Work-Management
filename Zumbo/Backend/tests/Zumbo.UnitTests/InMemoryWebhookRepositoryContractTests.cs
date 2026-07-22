using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.WorkItems;
using Zumbo.RepositoryContracts;
using ApplicationPersistence = Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.UnitTests;

public sealed class InMemoryWebhookRepositoryContractTests : WebhookRepositoryContract
{
    private readonly InMemoryDocumentRepository<WebhookSubscriptionDocument> subscriptions = new();
    private readonly InMemoryDocumentRepository<WebhookDeliveryDocument> deliveries = new();

    protected override ApplicationPersistence.IDocumentRepository<WebhookSubscriptionDocument> Subscriptions() => subscriptions;
    protected override ApplicationPersistence.IDocumentRepository<WebhookDeliveryDocument> Deliveries() => deliveries;
}
