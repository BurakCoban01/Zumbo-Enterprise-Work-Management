using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Organizations;

public sealed class OrganizationLifecycleOptions
{
    public int ArchiveRetentionDays { get; set; } = 90;
}
