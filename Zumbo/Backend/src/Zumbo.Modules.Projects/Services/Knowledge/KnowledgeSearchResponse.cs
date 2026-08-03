using System.Text.RegularExpressions;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed record KnowledgeSearchResponse(
    IReadOnlyCollection<KnowledgeDocumentSummaryResponse> Items,
    int Page,
    int PageSize,
    long VisibleTotal,
    int ScannedDocuments,
    string SourceStatus);
