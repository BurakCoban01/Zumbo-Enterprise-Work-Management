using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemTemplateRecurrenceService{

    private static void EnsureOwnership(
        string actualOrganizationId,
        string actualProjectId,
        string expectedOrganizationId,
        string expectedProjectId)
    {
        if (actualOrganizationId != expectedOrganizationId || actualProjectId != expectedProjectId)
        {
            throw new NotFoundException("WORK_ITEM_TEMPLATE_NOT_FOUND", "Work item template was not found.");
        }
    }
}
