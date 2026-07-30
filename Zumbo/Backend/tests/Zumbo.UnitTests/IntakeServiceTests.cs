using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.UnitTests;

public sealed class IntakeServiceTests
{
    private readonly InMemoryDocumentRepository<IntakeFormDocument> forms = new();
    private readonly InMemoryDocumentRepository<IntakeFormVersionDocument> versions = new();
    private readonly InMemoryDocumentRepository<IntakeSubmissionDocument> submissions = new();
    private readonly FixedClock clock = new();
    private readonly FixedCurrentUser currentUser = new();
    private readonly NoOpAuditPublisher audit = new();
    private readonly AllowPermissionChecker permissions = new();
    private readonly AllowRoutePolicy routes = new();

    [Fact]
    public async Task PublishedVersions_AreImmutableAndDraftChangesRequireANewVersion()
    {
        var service = CreateFormService();
        var created = await service.CreateAsync(
            new CreateIntakeFormRequest(
                "project-1",
                "Service request",
                "Initial description",
                Definition(IntakeAccessPolicies.Public, "Summary")),
            "correlation-1",
            default);
        var firstPublish = await service.PublishAsync(created.Id, "correlation-2", default);
        Assert.Equal(1, firstPublish.PublishedVersion);

        await service.UpdateAsync(
            created.Id,
            new UpdateIntakeFormRequest(
                "Service request",
                "Updated description",
                Definition(IntakeAccessPolicies.Public, "Request summary")),
            "correlation-3",
            default);
        var secondPublish = await service.PublishAsync(created.Id, "correlation-4", default);

        Assert.Equal(2, secondPublish.PublishedVersion);
        var firstVersion = await versions.SelectAsync(
            x => x.Id == IntakeStableIds.FormVersionId(created.Id, 1));
        var secondVersion = await versions.SelectAsync(
            x => x.Id == IntakeStableIds.FormVersionId(created.Id, 2));
        Assert.Equal("Summary", firstVersion!.Definition.Fields.Single(x => x.Key == "summary").Label);
        Assert.Equal("Request summary", secondVersion!.Definition.Fields.Single(x => x.Key == "summary").Label);
        Assert.Equal(1, firstVersion.Version);
        Assert.Equal(1, secondVersion.Version);
    }

    [Fact]
    public async Task PublicSubmission_IsIdempotentMappedAndAttachmentSafe()
    {
        var formService = CreateFormService();
        var form = await formService.CreateAsync(
            new CreateIntakeFormRequest(
                "project-1",
                "Service request",
                null,
                Definition(IntakeAccessPolicies.Public, "Summary")),
            "correlation-1",
            default);
        form = await formService.PublishAsync(form.Id, "correlation-2", default);
        var creator = new CapturingWorkItemCreator();
        var storage = new CapturingAttachmentStorage();
        var service = CreateSubmissionService(formService, creator, storage);
        var request = new CreateIntakeSubmissionRequest(
        [
            new("summary", "  Laptop cannot connect  "),
            new("details", "Network access fails after login."),
            new("severity", "high"),
            new("needed_by", "2026-08-01")
        ]);

        var first = await service.SubmitAsync(
            form.PublicId!,
            publicAccess: true,
            request,
            [Upload("evidence", "trace.txt", "text/plain", "connection refused")],
            "request-key-1",
            "correlation-3",
            default);
        var second = await service.SubmitAsync(
            form.PublicId!,
            publicAccess: true,
            request,
            [Upload("evidence", "trace.txt", "text/plain", "connection refused")],
            "request-key-1",
            "correlation-4",
            default);

        Assert.Equal(first, second);
        Assert.Equal(IntakeSubmissionStates.New, first.State);
        Assert.StartsWith("ZMB-", first.ConfirmationCode, StringComparison.Ordinal);
        Assert.Single(creator.Calls);
        Assert.Equal("project-1", creator.Calls[0].Request.ProjectId);
        Assert.Equal("board-1", creator.Calls[0].Request.BoardId);
        Assert.Equal("Laptop cannot connect", creator.Calls[0].Request.Title);
        Assert.Equal("Network access fails after login.", creator.Calls[0].Description);
        Assert.Equal("High", creator.Calls[0].Request.Priority);
        Assert.Equal(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero), creator.Calls[0].Request.DueDate);
        Assert.Single(creator.Calls[0].Attachments);
        Assert.Equal(1, storage.SaveCount);
        Assert.Equal(0, storage.DeleteCount);

        var conflict = await Assert.ThrowsAsync<ConflictException>(() => service.SubmitAsync(
            form.PublicId!,
            publicAccess: true,
            request with
            {
                Values =
                [
                    new("summary", "Different request"),
                    new("severity", "High")
                ]
            },
            [Upload("evidence", "trace.txt", "text/plain", "connection refused")],
            "request-key-1",
            "correlation-5",
            default));
        Assert.Equal("IDEMPOTENCY_KEY_REUSED", conflict.Code);
        Assert.Single(creator.Calls);
    }

    [Fact]
    public async Task PartialUploadCleanup_DoesNotHidePrimaryFailureAndUsesBoundedToken()
    {
        var formService = CreateFormService();
        var form = await formService.CreateAsync(
            new CreateIntakeFormRequest(
                "project-1",
                "Service request",
                null,
                Definition(IntakeAccessPolicies.Public, "Summary")),
            "correlation-1",
            default);
        form = await formService.PublishAsync(form.Id, "correlation-2", default);
        var storage = new CapturingAttachmentStorage
        {
            FailSaveOnAttempt = 2,
            FailDelete = true
        };
        var service = CreateSubmissionService(
            formService,
            new CapturingWorkItemCreator(),
            storage);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SubmitAsync(
            form.PublicId!,
            publicAccess: true,
            new CreateIntakeSubmissionRequest(
            [
                new("summary", "Cannot complete request")
            ]),
            [
                Upload("evidence", "first.txt", "text/plain", "first"),
                Upload("evidence", "second.txt", "text/plain", "second")
            ],
            "request-key-cleanup",
            "correlation-3",
            default));

        Assert.Equal("Synthetic primary save failure.", exception.Message);
        Assert.Equal(1, storage.DeleteCount);
        Assert.True(storage.DeleteToken.CanBeCanceled);
    }

    [Fact]
    public async Task AccessPolicyAndTriage_AreEnforced()
    {
        var formService = CreateFormService();
        var internalForm = await formService.CreateAsync(
            new CreateIntakeFormRequest(
                "project-1",
                "Internal request",
                null,
                Definition(IntakeAccessPolicies.Internal, "Summary", attachmentRequired: false)),
            "correlation-1",
            default);
        internalForm = await formService.PublishAsync(internalForm.Id, "correlation-2", default);
        var service = CreateSubmissionService(
            formService,
            new CapturingWorkItemCreator(),
            new CapturingAttachmentStorage());

        await Assert.ThrowsAsync<NotFoundException>(() => formService.GetPublishedAsync(
            internalForm.PublicId ?? "not-public",
            publicAccess: true,
            default));
        var confirmation = await service.SubmitAsync(
            internalForm.Id,
            publicAccess: false,
            new CreateIntakeSubmissionRequest(
            [
                new("summary", "Internal access request"),
                new("severity", "Low")
            ]),
            [],
            "internal-key-1",
            "correlation-3",
            default);
        var triaged = await formService.TriageAsync(
            internalForm.Id,
            confirmation.SubmissionId,
            new TriageIntakeSubmissionRequest(IntakeSubmissionStates.InReview, "Owned by service desk."),
            "correlation-4",
            default);

        Assert.Equal(IntakeSubmissionStates.InReview, triaged.State);
        Assert.Equal("user-1", triaged.TriagedByUserId);
        Assert.Equal("Owned by service desk.", triaged.TriageNote);
        var page = await formService.ListSubmissionsAsync(
            internalForm.Id,
            IntakeSubmissionStates.InReview,
            1,
            20,
            default);
        Assert.Single(page.Items);
        Assert.Equal(1, page.TotalCount);
    }

    private IntakeFormService CreateFormService() => new(
        forms,
        versions,
        submissions,
        permissions,
        routes,
        audit,
        clock,
        currentUser,
        configuredOptions: Options.Create(new IntakeOptions()));

    private IntakeSubmissionService CreateSubmissionService(
        IntakeFormService formService,
        IIntakeWorkItemCreator creator,
        IAttachmentStorage storage) => new(
        submissions,
        formService,
        routes,
        creator,
        storage,
        audit,
        clock,
        currentUser,
        Options.Create(new IntakeOptions()));

    private static IntakeFormDefinitionRequest Definition(
        string accessPolicy,
        string summaryLabel,
        bool attachmentRequired = true) => new(
        accessPolicy,
        "board-1",
        "Task",
        "Medium",
        "Your request is in the triage queue.",
        [
            new("summary", summaryLabel, IntakeFieldTypes.Text, Required: true),
            new("details", "Details", IntakeFieldTypes.LongText),
            new("severity", "Severity", IntakeFieldTypes.Choice, Options: ["Low", "High"]),
            new("needed_by", "Needed by", IntakeFieldTypes.Date),
            new("evidence", "Evidence", IntakeFieldTypes.Attachment, Required: attachmentRequired)
        ],
        new IntakeFieldMappingRequest(
            "summary",
            "details",
            "severity",
            "needed_by"));

    private static IntakeAttachmentUpload Upload(
        string fieldKey,
        string fileName,
        string contentType,
        string content)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        return new IntakeAttachmentUpload(
            fieldKey,
            new MemoryStream(bytes),
            fileName,
            contentType,
            bytes.Length);
    }

    private sealed class AllowPermissionChecker : IProjectPermissionChecker
    {
        public Task<ProjectResourceAuthorization> EnsureCanAsync(
            string userId,
            string projectId,
            string permission,
            CancellationToken ct) =>
            Task.FromResult(new ProjectResourceAuthorization(
                projectId,
                "org-1",
                userId,
                "ProjectOwner",
                false));
    }

    private sealed class AllowRoutePolicy : IIntakeRoutePolicy
    {
        public Task<IntakeRouteAuthorization> ValidateAsync(
            string organizationId,
            string projectId,
            string boardId,
            CancellationToken ct) =>
            organizationId == "org-1" && projectId == "project-1" && boardId == "board-1"
                ? Task.FromResult(new IntakeRouteAuthorization(organizationId, projectId, boardId))
                : Task.FromException<IntakeRouteAuthorization>(
                    new NotFoundException("INTAKE_ROUTE_NOT_FOUND", "Route was not found."));
    }

    private sealed class CapturingWorkItemCreator : IIntakeWorkItemCreator
    {
        public List<IntakeWorkItemCreation> Calls { get; } = [];

        public Task<WorkItemResponse> CreateAsync(
            IntakeWorkItemCreation creation,
            CancellationToken ct)
        {
            Calls.Add(creation);
            return Task.FromResult(new WorkItemResponse(
                Id: IntakeStableIds.WorkItemId(creation.SubmissionId),
                ProjectId: creation.Request.ProjectId,
                BoardId: creation.Request.BoardId,
                ParentId: null,
                TeamId: null,
                ColumnId: "todo",
                Title: creation.Request.Title,
                Description: creation.Description,
                Type: creation.Request.Type,
                Priority: creation.Request.Priority,
                Status: "To Do",
                AssigneeUserId: null,
                DueDate: creation.Request.DueDate,
                SprintId: null,
                EstimatePoints: 0,
                CompletedAt: null,
                StatusHistory: [],
                Labels: [],
                Checklist: [],
                Comments: [],
                Attachments: [],
                WorkLogs: [],
                Relations: [],
                Approvals: []));
        }
    }

    private sealed class CapturingAttachmentStorage : IAttachmentStorage
    {
        public int SaveCount { get; private set; }
        public int DeleteCount { get; private set; }
        public int? FailSaveOnAttempt { get; init; }
        public bool FailDelete { get; init; }
        public CancellationToken DeleteToken { get; private set; }

        public async Task<StoredAttachment> SaveAsync(
            Stream content,
            string fileName,
            string contentType,
            long maxSizeBytes,
            CancellationToken ct)
        {
            SaveCount++;
            if (SaveCount == FailSaveOnAttempt)
            {
                throw new InvalidOperationException("Synthetic primary save failure.");
            }
            using var hash = SHA256.Create();
            var checksum = Convert.ToHexString(await hash.ComputeHashAsync(content, ct)).ToLowerInvariant();
            return new StoredAttachment(
                fileName,
                contentType,
                content.Length,
                "attachments/" + checksum,
                checksum);
        }

        public Task<StoredAttachment> ReprocessAsync(
            StoredAttachment attachment,
            CancellationToken ct) => Task.FromResult(attachment);

        public Task<Stream> OpenReadAsync(
            string storagePath,
            string contentType,
            string expectedChecksumSha256,
            CancellationToken ct) =>
            Task.FromResult<Stream>(new MemoryStream());

        public Task<IReadOnlyList<StoredAttachmentObject>> ListObjectsAsync(
            int maxCount,
            CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<StoredAttachmentObject>>([]);

        public Task DeleteAsync(string storagePath, CancellationToken ct)
        {
            DeleteCount++;
            DeleteToken = ct;
            if (FailDelete)
            {
                throw new InvalidOperationException("Synthetic cleanup failure.");
            }
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpAuditPublisher : IWorkItemAuditPublisher
    {
        public Task WriteAsync(
            string action,
            string entityType,
            string entityId,
            string? oldValue,
            string? newValue,
            string correlationId,
            CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class FixedCurrentUser : ICurrentUser
    {
        public string? UserId => "user-1";
        public string? OrganizationId => "org-1";
        public IReadOnlyCollection<string> Roles => ["User"];
    }
}
