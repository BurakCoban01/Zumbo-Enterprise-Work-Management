using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Organizations;

public sealed record DepartmentResponse(
    string Id,
    string Name,
    string? ParentDepartmentId,
    IReadOnlyCollection<DepartmentMemberResponse> Members);
