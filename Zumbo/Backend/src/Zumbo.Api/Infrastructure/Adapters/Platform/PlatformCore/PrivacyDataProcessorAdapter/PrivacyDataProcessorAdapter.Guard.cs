using System.Linq.Expressions;
using System.Text;
using System.Text.Json;
using Zumbo.Api.Infrastructure.Adapters.Platform.PlatformCore.PrivacyDataProcessorAdapter;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Audit;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Notifications;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.Projects;
using Zumbo.Modules.Teams;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

public sealed partial class PrivacyDataProcessorAdapter{

    public async Task EnsureCanAnonymizeAsync(string userId, string organizationId, CancellationToken ct)
    {
        await anonymizationGuard.EnsureCanAnonymizeAsync(userId, organizationId, ct);
    }
}
