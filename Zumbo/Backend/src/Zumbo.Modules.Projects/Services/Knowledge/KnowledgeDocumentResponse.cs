using System.Text.RegularExpressions;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed record KnowledgeDocumentResponse(
    string Id,
    string ScopeType,
    string ScopeId,
    string ScopeName,
    string OwnerUserId,
    string Title,
    string ContentMarkdown,
    IReadOnlyCollection<string> Tags,
    IReadOnlyCollection<string> WorkItemIds,
    IReadOnlyCollection<string> UserIds,
    int CurrentContentVersion,
    IReadOnlyCollection<KnowledgeVersionSummaryResponse> Versions,
    IReadOnlyCollection<KnowledgeCommentResponse> Comments,
    bool CanEdit,
    bool CanComment,
    bool Archived,
    DateTimeOffset UpdatedAt,
    long Version) : IVersionedResource;
