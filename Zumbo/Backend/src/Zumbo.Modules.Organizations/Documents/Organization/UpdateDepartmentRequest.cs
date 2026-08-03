using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Organizations;
public sealed record UpdateDepartmentRequest(string Name, string? ParentDepartmentId);
