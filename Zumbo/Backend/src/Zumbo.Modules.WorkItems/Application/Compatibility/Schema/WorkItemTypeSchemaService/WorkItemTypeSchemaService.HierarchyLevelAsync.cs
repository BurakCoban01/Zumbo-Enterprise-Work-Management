using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemTypeSchemaService{

    public async Task<string> HierarchyLevelAsync(
        string projectId,
        string issueTypeKey,
        CancellationToken ct) =>
        FindActiveIssueType(await LoadOrDefaultAsync(projectId, ct), issueTypeKey).HierarchyLevel;
}
