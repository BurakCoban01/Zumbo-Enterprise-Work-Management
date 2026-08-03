using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Organizations;
public sealed record CreateDepartmentRequest(string Name, string? ParentDepartmentId);
