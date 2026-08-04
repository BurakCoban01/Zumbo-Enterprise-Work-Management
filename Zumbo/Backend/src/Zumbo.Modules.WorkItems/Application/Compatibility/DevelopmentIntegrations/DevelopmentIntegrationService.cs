using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems.Application.Features.Development.Connections;
using Zumbo.Modules.WorkItems.Application.Features.Development.Credentials;
using Zumbo.Modules.WorkItems.Application.Features.Development.ProviderHealth;
using Zumbo.Modules.WorkItems.Application.Features.Development.Repositories;
using Zumbo.Modules.WorkItems.Application.Features.Development.Mappings;
using Zumbo.Modules.WorkItems.Application.Features.Development.Links;
using Zumbo.Modules.WorkItems.Application.Features.Development.Webhooks;
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
    private readonly CheckProviderHealthHandler checkProviderHealthHandler = new(
        connections,
        credentialProtector,
        authorization,
        providerGateway,
        audit,
        clock,
        currentUser);
    private readonly ListRepositoriesHandler listRepositoriesHandler = new(
        connections,
        credentialProtector,
        authorization,
        providerGateway,
        currentUser);
    private readonly ListConnectionsHandler listConnectionsHandler = new(
        connections,
        authorization,
        currentUser);
    private readonly GetConnectionHandler getConnectionHandler = new(
        connections,
        authorization,
        currentUser);
    private readonly CreateConnectionHandler createConnectionHandler = new(
        connections,
        credentialProtector,
        authorization,
        providerGateway,
        audit,
        clock,
        currentUser);
    private readonly RotateCredentialHandler rotateCredentialHandler = new(
        connections,
        credentialProtector,
        authorization,
        audit,
        clock,
        currentUser);
    private readonly RotateWebhookSecretHandler rotateWebhookSecretHandler = new(
        connections,
        credentialProtector,
        authorization,
        audit,
        clock,
        currentUser);
    private readonly DisconnectConnectionHandler disconnectConnectionHandler = new(
        connections,
        mappings,
        authorization,
        audit,
        clock,
        currentUser);
    private readonly ListConnectionMappingsHandler listConnectionMappingsHandler = new(
        connections,
        mappings,
        authorization,
        currentUser);
    private readonly CreateMappingHandler createMappingHandler = new(
        connections,
        mappings,
        authorization,
        projectDirectory,
        projectPermissions,
        audit,
        clock,
        currentUser);
    private readonly DeleteMappingHandler deleteMappingHandler = new(
        mappings,
        links,
        authorization,
        projectPermissions,
        audit,
        currentUser);
    private readonly ListWorkItemMappingsHandler listWorkItemMappingsHandler = new(
        workItems,
        mappings,
        projectPermissions,
        currentUser);
    private readonly ListWorkItemLinksHandler listWorkItemLinksHandler = new(
        workItems,
        links,
        connections,
        projectPermissions,
        currentUser);
    private readonly CreateWorkItemLinkHandler createWorkItemLinkHandler = new(
        workItems,
        mappings,
        connections,
        links,
        projectPermissions,
        audit,
        clock,
        currentUser);
    private readonly DeleteWorkItemLinkHandler deleteWorkItemLinkHandler = new(
        workItems,
        links,
        projectPermissions,
        audit,
        currentUser);
    private readonly ReceiveWebhookHandler receiveWebhookHandler = new(
        connections,
        receipts,
        credentialProtector,
        webhookQueue,
        clock);
    private readonly ProcessWebhookHandler processWebhookHandler = new(
        receipts,
        connections,
        mappings,
        new ApplyWebhookLinksHandler(links, workItems, clock),
        audit);
    private readonly DeleteConnectionHandler deleteConnectionHandler = new(
        connections,
        mappings,
        links,
        receipts,
        authorization,
        audit,
        currentUser);
}
