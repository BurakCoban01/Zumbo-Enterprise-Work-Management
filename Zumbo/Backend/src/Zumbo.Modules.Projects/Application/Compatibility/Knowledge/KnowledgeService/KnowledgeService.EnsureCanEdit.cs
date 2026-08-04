using System.Text.RegularExpressions;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class KnowledgeService{

    private static void EnsureCanEdit(
        KnowledgeDocument document,
        KnowledgeScopeAccess access,
        string userId)
    {
        if (document.OwnerUserId != userId && !access.CanManage)
            throw new ForbiddenException("Knowledge document edit access is required.");
    }
}
