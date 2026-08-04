using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Audit;
using Zumbo.Modules.Identity;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

public sealed class DashboardViewerDirectoryAdapter(
    IDocumentRepository<UserDocument> users) : IDashboardViewerDirectory
{
    public async Task EnsureOrganizationUsersAsync(
        string organizationId,
        IReadOnlyCollection<string> userIds,
        CancellationToken ct)
    {
        foreach (var userId in userIds)
        {
            if (!await users.ExistsByFilterAsync(
                    user => user.Id == userId
                        && user.OrganizationId == organizationId
                        && user.IsActive,
                    ct))
            {
                throw new ValidationException(
                    "Dashboard viewers must be active users in the dashboard organization.");
            }
        }
    }
}
