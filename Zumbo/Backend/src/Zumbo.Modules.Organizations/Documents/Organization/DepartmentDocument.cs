using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Organizations;

public sealed class DepartmentDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string? ParentDepartmentId { get; set; }
    public List<DepartmentMemberDocument> Members { get; set; } = [];
}
