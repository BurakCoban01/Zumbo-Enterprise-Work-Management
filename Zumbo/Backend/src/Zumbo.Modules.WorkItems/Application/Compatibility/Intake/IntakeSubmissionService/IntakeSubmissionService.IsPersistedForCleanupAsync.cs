using System.Globalization;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Runtime;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class IntakeSubmissionService{

    private async Task<bool> IsPersistedForCleanupAsync(
        string submissionId,
        string organizationId,
        string formId)
    {
        var persisted = true;
        var result = await CompensationExecution.RunAsync(
            "intake.submission.exists",
            async token =>
            {
                persisted = await submissions.ExistsByFilterAsync(
                    x => x.Id == submissionId
                        && x.OrganizationId == organizationId
                        && x.FormId == formId,
                    token);
            });
        ObserveCompensation(result);
        return result.Succeeded ? persisted : true;
    }
}
