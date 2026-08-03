using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Organizations;

public sealed class DepartmentMemberDocument
{
    public string UserId { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
}
