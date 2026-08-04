using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record AttachmentMaintenanceResult(
    int Retried,
    int Cleaned,
    int Rejected,
    int PurgedMetadata,
    int DeletedOrphans);
