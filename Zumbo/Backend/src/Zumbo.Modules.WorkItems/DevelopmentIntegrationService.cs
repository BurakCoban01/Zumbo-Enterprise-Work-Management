using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class DevelopmentIntegrationService(
    IDocumentRepository<DevelopmentConnectionDocument> connections,
    IDocumentRepository<DevelopmentRepositoryMappingDocument> mappings,
    IDocumentRepository<WorkItemDevelopmentLinkDocument> links,
    IDocumentRepository<DevelopmentWebhookReceiptDocument> receipts,
    IDocumentRepository<WorkItemDocument> workItems,
    IDevelopmentCredentialProtector credentialProtector,
    IDevelopmentIntegrationAuthorization authorization,
    IDevelopmentProjectDirectory projectDirectory,
    IDevelopmentProviderGateway providerGateway,
    IDevelopmentWebhookQueue webhookQueue,
    IProjectPermissionChecker projectPermissions,
    IWorkItemAuditPublisher audit,
    IClock clock,
    ICurrentUser currentUser)
{

}
