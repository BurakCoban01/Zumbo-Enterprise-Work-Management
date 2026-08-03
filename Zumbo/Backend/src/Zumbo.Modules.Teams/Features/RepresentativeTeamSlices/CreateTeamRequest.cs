using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Teams;

public sealed record CreateTeamRequest(string OrganizationId, string Name, string OwnerUserId);
