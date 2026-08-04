using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class PortfolioService{

    private static SaveInitiativeRequest Normalize(SaveInitiativeRequest request)
    {
        var projectIds = NormalizeIds(
            request.ProjectIds,
            MaximumProjectsPerInitiative,
            "Initiative project");
        var links = (request.MilestoneLinks
                ?? throw new ValidationException("Initiative milestone links are required."))
            .Select(link => new PortfolioMilestoneLinkRequest(
                Required(link.ProjectId, "Milestone project", 128),
                Required(link.MilestoneId, "Milestone", 128)))
            .Distinct()
            .ToList();
        if (links.Count > 50)
            throw new ValidationException("An initiative cannot contain more than 50 milestone links.");
        if (links.Any(link => !projectIds.Contains(link.ProjectId)))
            throw new ValidationException("Milestone links must belong to an initiative project.");
        return request with
        {
            Name = Required(request.Name, "Initiative name", 120),
            Summary = Optional(request.Summary, 1000),
            ParentInitiativeId = Optional(request.ParentInitiativeId, 128),
            OwnerUserId = Required(request.OwnerUserId, "Initiative owner", 128),
            Status = Allowed(request.Status, InitiativeStatuses.Allowed, "Initiative status"),
            Health = Allowed(request.Health, InitiativeHealth.Allowed, "Initiative health"),
            Confidence = Confidence(request.Confidence),
            ProjectIds = projectIds,
            MilestoneLinks = links
        };
    }
}
