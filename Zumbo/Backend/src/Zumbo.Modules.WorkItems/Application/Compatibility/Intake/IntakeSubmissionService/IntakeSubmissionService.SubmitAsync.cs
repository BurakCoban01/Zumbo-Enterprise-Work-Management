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

    public async Task<IntakeSubmissionConfirmationResponse> SubmitAsync(
        string identifier,
        bool publicAccess,
        CreateIntakeSubmissionRequest request,
        IReadOnlyCollection<IntakeAttachmentUpload> attachmentUploads,
        string idempotencyKey,
        string correlationId,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(request.Website))
        {
            throw new ValidationException("Submission could not be accepted.");
        }

        var version = await forms.ResolveSubmissionVersionAsync(identifier, publicAccess, ct);
        await routePolicy.ValidateAsync(
            version.OrganizationId,
            version.ProjectId,
            version.Definition.BoardId,
            ct);
        var submittedBy = publicAccess
            ? "public"
            : currentUser.UserId ?? throw new UnauthorizedException("Authenticated user is required.");
        var values = NormalizeValues(version.Definition, request.Values);
        var mapped = MapWorkItem(version.Definition, values);
        mapped = mapped with
        {
            Request = mapped.Request with { ProjectId = version.ProjectId }
        };
        CreateWorkItemValidator.Validate(mapped.Request);
        if (mapped.Description.Length > 10_000)
        {
            throw new ValidationException(
                "Mapped work item description cannot exceed 10000 characters.");
        }
        var uploads = attachmentUploads?.ToList() ?? [];
        ValidateAttachmentShape(version.Definition, values, uploads);
        var keyHash = IntakeStableIds.Hash(NormalizeIdempotencyKey(idempotencyKey));
        var attachmentFingerprints = await FingerprintAttachmentsAsync(uploads, ct);
        var fingerprint = Fingerprint(version, values, attachmentFingerprints);
        var submissionId = IntakeStableIds.SubmissionId(
            version.OrganizationId,
            version.FormId,
            version.DefinitionVersion,
            submittedBy,
            keyHash);

        var existing = await submissions.SelectAsync(
            x => x.Id == submissionId
                && x.OrganizationId == version.OrganizationId
                && x.FormId == version.FormId,
            ct);
        if (existing is not null)
        {
            EnsureSameRequest(existing, fingerprint);
            return await CompleteAsync(existing, version, correlationId, ct);
        }

        var stored = new List<IntakeSubmissionAttachmentDocument>();
        try
        {
            for (var index = 0; index < uploads.Count; index++)
            {
                var upload = uploads[index];
                var saved = await attachmentStorage.SaveAsync(
                    upload.Content,
                    upload.FileName,
                    upload.ContentType,
                    options.MaxAttachmentBytes,
                    ct);
                stored.Add(new IntakeSubmissionAttachmentDocument
                {
                    FieldKey = RequiredKey(upload.FieldKey),
                    FileName = saved.FileName,
                    ContentType = saved.ContentType,
                    SizeBytes = saved.SizeBytes,
                    StoragePath = saved.StoragePath,
                    ChecksumSha256 = saved.ChecksumSha256,
                    SecurityState = saved.SecurityState,
                    ScanProvider = saved.ScanProvider,
                    ScanDetail = saved.ScanDetail,
                    ScannedAt = saved.ScannedAt,
                    CreatedAt = clock.UtcNow
                });
            }

            var now = clock.UtcNow;
            var submission = new IntakeSubmissionDocument
            {
                Id = submissionId,
                OrganizationId = version.OrganizationId,
                FormId = version.FormId,
                FormVersion = version.DefinitionVersion,
                ProjectId = version.ProjectId,
                BoardId = version.Definition.BoardId,
                AccessPolicy = version.Definition.AccessPolicy,
                SubmittedByUserId = submittedBy,
                IdempotencyKeyHash = keyHash,
                RequestFingerprint = fingerprint,
                ConfirmationCode = IntakeStableIds.ConfirmationCode(submissionId),
                WorkItemId = IntakeStableIds.WorkItemId(submissionId),
                Values = values,
                Attachments = stored,
                CreatedAt = now,
                UpdatedAt = now
            };
            try
            {
                await submissions.CreateAsync(submission, ct);
            }
            catch (DocumentConflictException exception)
            {
                await TryDeleteStoredAsync(stored);
                var raced = await submissions.SelectAsync(
                    x => x.Id == submissionId
                        && x.OrganizationId == version.OrganizationId
                        && x.FormId == version.FormId,
                    ct);
                if (raced is null)
                {
                    throw new DocumentConflictException(
                        "The intake submission conflicted but could not be reloaded.",
                        exception);
                }
                EnsureSameRequest(raced, fingerprint);
                return await CompleteAsync(raced, version, correlationId, ct);
            }

            await audit.WriteAsync(
                "IntakeSubmissionReceived",
                "IntakeSubmission",
                submission.Id,
                null,
                $"{submission.FormId}:{submission.FormVersion}",
                correlationId,
                ct);
            return await CompleteAsync(submission, version, correlationId, ct);
        }
        catch
        {
            var persisted = await IsPersistedForCleanupAsync(
                submissionId,
                version.OrganizationId,
                version.FormId);
            if (!persisted)
            {
                await TryDeleteStoredAsync(stored);
            }
            throw;
        }
    }
}
