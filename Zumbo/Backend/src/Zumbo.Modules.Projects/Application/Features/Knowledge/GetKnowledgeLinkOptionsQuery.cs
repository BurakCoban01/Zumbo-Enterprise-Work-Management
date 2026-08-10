namespace Zumbo.Modules.Projects.Application.Features.Knowledge;

public sealed record GetKnowledgeLinkOptionsQuery(
    string ScopeType,
    string ScopeId,
    string? Query);
