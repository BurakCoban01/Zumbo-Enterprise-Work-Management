using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.WorkItems;

public sealed class CapacityPlanDocument : IVersionedDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OrganizationId { get; set; } = string.Empty;
    public string OwnerUserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTimeOffset PeriodStartUtc { get; set; }
    public DateTimeOffset PeriodEndUtc { get; set; }
    public string? PortfolioId { get; set; }
    public List<string> ProjectIds { get; set; } = [];
    public List<CapacityMemberDocument> Members { get; set; } = [];
    public List<CapacityAllocationDocument> Allocations { get; set; } = [];
    public List<string> ViewerUserIds { get; set; } = [];
    public bool Archived { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; }
}
