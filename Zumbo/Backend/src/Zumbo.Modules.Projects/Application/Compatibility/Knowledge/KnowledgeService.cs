using System.Text.RegularExpressions;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Projects.Application.Features.Knowledge;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class KnowledgeService(
    IDocumentRepository<KnowledgeDocument> documents,
    IKnowledgeDirectory directory,
    IKnowledgeAuditWriter audit,
    ICurrentUser currentUser,
    IClock clock,
    IExpectedVersionAccessor? expectedVersions = null)
{
    private readonly ExpectedVersionState expectedVersion = new(expectedVersions);
    private readonly GetKnowledgeDocumentHandler getKnowledgeDocumentHandler =
        new(documents, directory, currentUser);
    private readonly GetKnowledgeVersionHandler getKnowledgeVersionHandler =
        new(documents, directory, currentUser);
    private readonly GetKnowledgeLinkOptionsHandler getKnowledgeLinkOptionsHandler =
        new(directory, currentUser);
    private readonly SearchKnowledgeDocumentsHandler searchKnowledgeDocumentsHandler =
        new(documents, directory, currentUser);
    private readonly AddKnowledgeCommentHandler addKnowledgeCommentHandler =
        new(documents, directory, audit, currentUser, clock, expectedVersions);
    private readonly ResolveKnowledgeCommentHandler resolveKnowledgeCommentHandler =
        new(documents, directory, audit, currentUser, clock, expectedVersions);
    private readonly CreateKnowledgeDocumentHandler createKnowledgeDocumentHandler =
        new(documents, directory, audit, currentUser, clock);
    private readonly AddKnowledgeVersionHandler addKnowledgeVersionHandler =
        new(documents, directory, audit, currentUser, clock, expectedVersions);
    private readonly ArchiveKnowledgeDocumentHandler archiveKnowledgeDocumentHandler =
        new(documents, directory, audit, currentUser, clock, expectedVersions);
}
