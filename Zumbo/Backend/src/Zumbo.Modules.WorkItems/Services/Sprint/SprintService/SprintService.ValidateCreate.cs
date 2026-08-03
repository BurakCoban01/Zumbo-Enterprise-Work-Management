using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems.Domain;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class SprintService{

    private static void ValidateCreate(CreateSprintRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectId))
        {
            throw new ValidationException("Project id is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 120)
        {
            throw new ValidationException("Sprint name is required and cannot exceed 120 characters.");
        }

        if (request.Goal?.Trim().Length > 500)
        {
            throw new ValidationException("Sprint goal cannot exceed 500 characters.");
        }

        var days = request.EndDate.DayNumber - request.StartDate.DayNumber + 1;
        if (days is < 1 or > 60)
        {
            throw new ValidationException("Sprint duration must be between 1 and 60 days.");
        }
    }
}
