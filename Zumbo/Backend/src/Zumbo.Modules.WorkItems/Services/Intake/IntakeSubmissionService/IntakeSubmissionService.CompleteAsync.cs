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

    private async Task<IntakeSubmissionConfirmationResponse> CompleteAsync(
        IntakeSubmissionDocument submission,
        IntakeFormVersionDocument version,
        string correlationId,
        CancellationToken ct)
    {
        if (submission.State != IntakeSubmissionStates.Processing)
        {
            return Confirmation(submission, version);
        }

        var mapped = MapWorkItem(version.Definition, submission.Values);
        mapped = mapped with
        {
            Request = mapped.Request with { ProjectId = version.ProjectId }
        };
        var attachments = submission.Attachments.Select(x => new StoredAttachment(
            x.FileName,
            x.ContentType,
            x.SizeBytes,
            x.StoragePath,
            x.ChecksumSha256,
            x.SecurityState,
            x.ScanProvider,
            x.ScanDetail,
            x.ScannedAt)).ToList();
        var workItem = await workItemCreator.CreateAsync(
            new IntakeWorkItemCreation(
                submission.OrganizationId,
                submission.Id,
                mapped.Request,
                mapped.Description,
                attachments,
                correlationId),
            ct);
        submission.WorkItemId = workItem.Id;
        submission.State = IntakeSubmissionStates.New;
        submission.UpdatedAt = clock.UtcNow;
        var result = await submissions.ReplaceByVersionAsync(
            x => x.Id == submission.Id
                && x.OrganizationId == submission.OrganizationId
                && x.FormId == submission.FormId
                && x.State == IntakeSubmissionStates.Processing,
            submission,
            submission.Version,
            ct);
        if (result.Found)
        {
            submission.Version = result.Version!.Value;
            await audit.WriteAsync(
                "IntakeSubmissionRouted",
                "IntakeSubmission",
                submission.Id,
                IntakeSubmissionStates.Processing,
                $"{submission.State}:{submission.WorkItemId}",
                correlationId,
                ct);
        }
        else
        {
            submission = await submissions.SelectAsync(
                x => x.Id == submission.Id
                    && x.OrganizationId == submission.OrganizationId
                    && x.FormId == submission.FormId,
                ct)
                ?? throw new NotFoundException(
                    "INTAKE_SUBMISSION_NOT_FOUND",
                    "Intake submission was not found.");
        }

        return Confirmation(submission, version);
    }
}
