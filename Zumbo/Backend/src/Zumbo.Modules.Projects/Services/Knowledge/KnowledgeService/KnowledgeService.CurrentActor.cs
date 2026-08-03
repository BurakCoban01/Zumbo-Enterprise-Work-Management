using System.Text.RegularExpressions;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class KnowledgeService{

    private (string UserId, string OrganizationId) CurrentActor() => (
        currentUser.UserId
            ?? throw new UnauthorizedException("Authenticated user is required."),
        currentUser.OrganizationId
            ?? throw new UnauthorizedException("Active organization is required."));
}
