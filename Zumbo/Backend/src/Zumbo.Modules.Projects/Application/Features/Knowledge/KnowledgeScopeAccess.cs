using System.Text.RegularExpressions;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed record KnowledgeScopeAccess(
    string OrganizationId,
    string ScopeName,
    IReadOnlyCollection<string> ProjectIds,
    bool CanManage,
    bool CanComment);
