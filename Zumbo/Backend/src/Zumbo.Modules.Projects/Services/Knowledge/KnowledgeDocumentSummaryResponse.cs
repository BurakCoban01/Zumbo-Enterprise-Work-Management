using System.Text.RegularExpressions;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed record KnowledgeDocumentSummaryResponse(
    string Id,
    string ScopeType,
    string ScopeId,
    string ScopeName,
    string OwnerUserId,
    string Title,
    string Excerpt,
    IReadOnlyCollection<string> Tags,
    int CurrentContentVersion,
    bool CanEdit,
    bool Archived,
    DateTimeOffset UpdatedAt,
    long Version);
