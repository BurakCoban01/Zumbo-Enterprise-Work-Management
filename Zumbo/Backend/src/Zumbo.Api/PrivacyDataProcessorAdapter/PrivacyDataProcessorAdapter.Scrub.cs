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

    private static string? Scrub(string? value, string username, string email)
    {
        if (value is null) return null;
        return value
            .Replace(username, "[anonymized]", StringComparison.OrdinalIgnoreCase)
            .Replace(email, "[anonymized]", StringComparison.OrdinalIgnoreCase);
    }
}
