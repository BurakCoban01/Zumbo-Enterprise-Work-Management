using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.Notifications;
using Zumbo.RepositoryContracts;

namespace Zumbo.UnitTests;

public sealed class InMemoryNotificationDeliveryRepositoryContractTests
    : NotificationDeliveryRepositoryContract
{
    protected override Zumbo.BuildingBlocks.Application.Persistence.IDocumentRepository<NotificationDocument> Repository() =>
        new InMemoryDocumentRepository<NotificationDocument>();
}
