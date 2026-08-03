using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed record CreateProjectRequest(
    string OrganizationId,
    string Key,
    string Name,
    string OwnerUserId,
    string Visibility = ProjectVisibilities.Internal);
