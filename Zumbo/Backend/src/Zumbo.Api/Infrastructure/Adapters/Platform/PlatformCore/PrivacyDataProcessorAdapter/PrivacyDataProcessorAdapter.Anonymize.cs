using System.Linq.Expressions;
using System.Text;
using System.Text.Json;
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

    public async Task AnonymizeReferencesAsync(
        string userId,
        string organizationId,
        string pseudonym,
        string username,
        string email,
        CancellationToken ct)
    {
        await anonymization.AnonymizeReferencesAsync(
            userId,
            organizationId,
            pseudonym,
            username,
            email,
            ct);
    }

    private static string? Scrub(string? value, string username, string email)
    {
        return PrivacyAnonymizationComponent.Scrub(value, username, email);
    }
}
