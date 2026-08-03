using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed record ListProjectsQuery(string OrganizationId, bool Archived);
