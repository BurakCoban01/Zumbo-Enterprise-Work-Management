namespace Zumbo.Modules.Projects.Application.Features.Knowledge;

public sealed record ArchiveKnowledgeDocumentCommand(
    string DocumentId,
    string CorrelationId);
