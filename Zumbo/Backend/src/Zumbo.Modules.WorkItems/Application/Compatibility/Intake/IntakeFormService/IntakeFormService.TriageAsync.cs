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

    public async Task<IntakeSubmissionResponse> TriageAsync(
        string formId,
        string submissionId,
        TriageIntakeSubmissionRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var form = await GetManagedAsync(formId, PermissionCatalog.WorkItemUpdate, ct);
        var submission = await submissions.SelectAsync(
            x => x.Id == submissionId
                && x.OrganizationId == form.OrganizationId
                && x.FormId == form.Id,
            ct)
            ?? throw new NotFoundException(
                "INTAKE_SUBMISSION_NOT_FOUND",
                "Intake submission was not found.");
        var nextState = NormalizeTriageState(request.State);
        if (submission.State == IntakeSubmissionStates.Processing)
        {
            throw new ConflictException(
                "INTAKE_SUBMISSION_PROCESSING",
                "A submission cannot be triaged until work creation completes.");
        }

        var oldState = submission.State;
        submission.State = nextState;
        submission.TriageNote = Optional(request.Note, 2_000);
        submission.TriagedByUserId = RequireUser();
        submission.TriagedAt = clock.UtcNow;
        submission.UpdatedAt = submission.TriagedAt.Value;
        await ReplaceSubmissionAsync(submission, ct);
        await audit.WriteAsync(
            "IntakeSubmissionTriaged",
            "IntakeSubmission",
            submission.Id,
            oldState,
            nextState,
            correlationId,
            ct);
        return ToSubmissionResponse(submission);
    }
}
