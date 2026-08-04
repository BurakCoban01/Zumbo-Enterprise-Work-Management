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

public sealed partial class IntakeFormService{

    private async Task ReplaceSubmissionAsync(
        IntakeSubmissionDocument submission,
        CancellationToken ct)
    {
        var result = await submissions.ReplaceByVersionAsync(
            x => x.Id == submission.Id
                && x.OrganizationId == submission.OrganizationId
                && x.FormId == submission.FormId,
            submission,
            expectedVersion.Consume(submission.Version),
            ct);
        if (!result.Found)
        {
            throw new NotFoundException(
                "INTAKE_SUBMISSION_NOT_FOUND",
                "Intake submission was not found.");
        }

        submission.Version = result.Version!.Value;
    }
}
