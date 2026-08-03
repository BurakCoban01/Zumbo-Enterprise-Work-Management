using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Organizations;
public sealed record AssignDepartmentMemberRequest(string UserId, string Position);
