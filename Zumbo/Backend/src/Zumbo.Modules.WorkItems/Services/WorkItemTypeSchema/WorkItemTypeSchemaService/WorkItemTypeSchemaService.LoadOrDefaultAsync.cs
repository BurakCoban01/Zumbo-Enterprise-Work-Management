using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemTypeSchemaService{

    private async Task<WorkItemTypeSchemaDocument> LoadOrDefaultAsync(string projectId, CancellationToken ct) =>
        await schemas.SelectAsync(schema => schema.ProjectId == projectId, ct) ?? Default(projectId, clock.UtcNow);
}
