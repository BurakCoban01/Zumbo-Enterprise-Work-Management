using System.Globalization;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class IntakeOptions
{
    public int MaxFields { get; init; } = 40;
    public int MaxValues { get; init; } = 40;
    public int MaxAttachments { get; init; } = 5;
    public long MaxAttachmentBytes { get; init; } = 10 * 1024 * 1024;
    public long MaxTotalAttachmentBytes { get; init; } = 25 * 1024 * 1024;
    public int MaxValueCharacters { get; init; } = 4_000;
    public int MaxTotalValueCharacters { get; init; } = 20_000;
}

public static class IntakeStableIds
{
    public static string FormVersionId(string formId, int version) =>
        Hash($"form-version\u001f{formId}\u001f{version}")[..32];

    public static string SubmissionId(
        string organizationId,
        string formId,
        int version,
        string submittedBy,
        string idempotencyKeyHash) =>
        Hash($"submission\u001f{organizationId}\u001f{formId}\u001f{version}\u001f{submittedBy}\u001f{idempotencyKeyHash}")[..32];

    public static string WorkItemId(string submissionId) =>
        Hash($"intake-work-item\u001f{submissionId}")[..32];

    public static string ConfirmationCode(string submissionId) =>
        "ZMB-" + submissionId[..8].ToUpperInvariant();

    public static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

public sealed class IntakeFormService(
    IDocumentRepository<IntakeFormDocument> forms,
    IDocumentRepository<IntakeFormVersionDocument> versions,
    IDocumentRepository<IntakeSubmissionDocument> submissions,
    IProjectPermissionChecker permissions,
    IIntakeRoutePolicy routePolicy,
    IWorkItemAuditPublisher audit,
    IClock clock,
    ICurrentUser currentUser,
    IExpectedVersionAccessor? expectedVersions = null,
    IOptions<IntakeOptions>? configuredOptions = null)
{
    private static readonly Regex KeyPattern = new(
        "^[a-z][a-z0-9_-]{0,39}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private readonly ExpectedVersionState expectedVersion = new(expectedVersions);
    private readonly IntakeOptions options = configuredOptions?.Value ?? new IntakeOptions();

    public async Task<IntakeFormResponse> CreateAsync(
        CreateIntakeFormRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var userId = RequireUser();
        var authorization = await permissions.EnsureCanAsync(
            userId,
            Required(request.ProjectId, "Project id", 128),
            PermissionCatalog.WorkflowManage,
            ct);
        var definition = NormalizeDefinition(request.Definition);
        await routePolicy.ValidateAsync(
            authorization.OrganizationId,
            authorization.ProjectId,
            definition.BoardId,
            ct);
        var now = clock.UtcNow;
        var document = new IntakeFormDocument
        {
            OrganizationId = authorization.OrganizationId,
            ProjectId = authorization.ProjectId,
            Name = Required(request.Name, "Form name", 120),
            Description = Optional(request.Description, 1_000),
            Draft = definition,
            CreatedByUserId = userId,
            UpdatedByUserId = userId,
            CreatedAt = now,
            UpdatedAt = now
        };
        await forms.CreateAsync(document, ct);
        await audit.WriteAsync(
            "IntakeFormCreated",
            "IntakeForm",
            document.Id,
            null,
            document.Name,
            correlationId,
            ct);
        return ToResponse(document);
    }

    public async Task<IntakeFormResponse> UpdateAsync(
        string formId,
        UpdateIntakeFormRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var form = await GetManagedAsync(formId, PermissionCatalog.WorkflowManage, ct);
        EnsureNotArchived(form);
        var definition = NormalizeDefinition(request.Definition);
        await routePolicy.ValidateAsync(
            form.OrganizationId,
            form.ProjectId,
            definition.BoardId,
            ct);
        var oldName = form.Name;
        form.Name = Required(request.Name, "Form name", 120);
        form.Description = Optional(request.Description, 1_000);
        form.Draft = definition;
        form.UpdatedByUserId = RequireUser();
        form.UpdatedAt = clock.UtcNow;
        await ReplaceAsync(form, ct);
        await audit.WriteAsync(
            "IntakeFormDraftUpdated",
            "IntakeForm",
            form.Id,
            oldName,
            form.Name,
            correlationId,
            ct);
        return ToResponse(form);
    }

    public async Task<IntakeFormResponse> PublishAsync(
        string formId,
        string correlationId,
        CancellationToken ct)
    {
        var form = await GetManagedAsync(formId, PermissionCatalog.WorkflowManage, ct);
        EnsureNotArchived(form);
        await routePolicy.ValidateAsync(
            form.OrganizationId,
            form.ProjectId,
            form.Draft.BoardId,
            ct);
        var nextVersion = checked(form.PublishedVersion + 1);
        var published = new IntakeFormVersionDocument
        {
            Id = IntakeStableIds.FormVersionId(form.Id, nextVersion),
            OrganizationId = form.OrganizationId,
            FormId = form.Id,
            ProjectId = form.ProjectId,
            DefinitionVersion = nextVersion,
            Name = form.Name,
            Description = form.Description,
            Definition = CloneDefinition(form.Draft),
            PublishedByUserId = RequireUser(),
            PublishedAt = clock.UtcNow
        };
        await versions.CreateAsync(published, ct);

        var oldState = form.State;
        form.State = IntakeFormStates.Published;
        form.PublishedVersion = nextVersion;
        form.PublishedAccessPolicy = published.Definition.AccessPolicy;
        form.PublishedAt = published.PublishedAt;
        form.UpdatedAt = published.PublishedAt;
        form.UpdatedByUserId = published.PublishedByUserId;
        await ReplaceAsync(form, ct);
        await audit.WriteAsync(
            "IntakeFormPublished",
            "IntakeForm",
            form.Id,
            $"{oldState}:{nextVersion - 1}",
            $"{form.State}:{nextVersion}",
            correlationId,
            ct);
        return ToResponse(form);
    }

    public async Task<IntakeFormResponse> ArchiveAsync(
        string formId,
        string correlationId,
        CancellationToken ct)
    {
        var form = await GetManagedAsync(formId, PermissionCatalog.WorkflowManage, ct);
        if (form.State == IntakeFormStates.Archived)
        {
            return ToResponse(form);
        }

        var oldState = form.State;
        form.State = IntakeFormStates.Archived;
        form.ArchivedAt = clock.UtcNow;
        form.UpdatedAt = form.ArchivedAt.Value;
        form.UpdatedByUserId = RequireUser();
        await ReplaceAsync(form, ct);
        await audit.WriteAsync(
            "IntakeFormArchived",
            "IntakeForm",
            form.Id,
            oldState,
            form.State,
            correlationId,
            ct);
        return ToResponse(form);
    }

    public async Task<IReadOnlyCollection<IntakeFormResponse>> ListAsync(
        string projectId,
        CancellationToken ct)
    {
        var authorization = await permissions.EnsureCanAsync(
            RequireUser(),
            Required(projectId, "Project id", 128),
            PermissionCatalog.WorkItemView,
            ct);
        var result = await forms.ListByFilterAsync(
            x => x.OrganizationId == authorization.OrganizationId
                && x.ProjectId == authorization.ProjectId,
            x => x.UpdatedAt,
            orderDescending: true,
            pageSize: 200,
            cancellationToken: ct);
        return result.Select(ToResponse).ToList();
    }

    public async Task<IntakeFormResponse> GetAsync(string formId, CancellationToken ct) =>
        ToResponse(await GetManagedAsync(formId, PermissionCatalog.WorkItemView, ct));

    public async Task<PublishedIntakeFormResponse> GetPublishedAsync(
        string identifier,
        bool publicAccess,
        CancellationToken ct)
    {
        var form = publicAccess
            ? await forms.SelectAsync(
                x => x.PublicId == identifier && x.State == IntakeFormStates.Published,
                ct)
            : await forms.SelectAsync(
                x => x.Id == identifier && x.State == IntakeFormStates.Published,
                ct);
        if (form is null)
        {
            throw new NotFoundException("INTAKE_FORM_NOT_FOUND", "Intake form was not found.");
        }

        if (publicAccess)
        {
            if (form.PublishedAccessPolicy != IntakeAccessPolicies.Public)
            {
                throw new NotFoundException("INTAKE_FORM_NOT_FOUND", "Intake form was not found.");
            }
        }
        else
        {
            _ = await permissions.EnsureCanAsync(
                RequireUser(),
                form.ProjectId,
                PermissionCatalog.WorkItemCreate,
                ct);
        }

        var version = await GetVersionAsync(form, ct);
        if (publicAccess && version.Definition.AccessPolicy != IntakeAccessPolicies.Public)
        {
            throw new NotFoundException("INTAKE_FORM_NOT_FOUND", "Intake form was not found.");
        }

        return ToPublishedResponse(version);
    }

    public async Task<IntakeSubmissionPage> ListSubmissionsAsync(
        string formId,
        string? state,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var form = await GetManagedAsync(formId, PermissionCatalog.WorkItemView, ct);
        var normalizedState = NormalizeOptionalSubmissionState(state);
        var safePage = Math.Max(page, 1);
        var safePageSize = Math.Clamp(pageSize, 1, 100);
        var filter = (System.Linq.Expressions.Expression<Func<IntakeSubmissionDocument, bool>>)(x =>
            x.OrganizationId == form.OrganizationId
            && x.FormId == form.Id
            && (normalizedState == null || x.State == normalizedState));
        var total = await submissions.CountByFilterAsync(filter, ct);
        var result = await submissions.ListByFilterAsync(
            filter,
            x => x.CreatedAt,
            orderDescending: true,
            page: safePage,
            pageSize: safePageSize,
            cancellationToken: ct);
        return new IntakeSubmissionPage(
            result.Select(ToSubmissionResponse).ToList(),
            safePage,
            safePageSize,
            total);
    }

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

    internal async Task<IntakeFormVersionDocument> ResolveSubmissionVersionAsync(
        string identifier,
        bool publicAccess,
        CancellationToken ct)
    {
        var form = publicAccess
            ? await forms.SelectAsync(
                x => x.PublicId == identifier && x.State == IntakeFormStates.Published,
                ct)
            : await forms.SelectAsync(
                x => x.Id == identifier && x.State == IntakeFormStates.Published,
                ct);
        if (form is null)
        {
            throw new NotFoundException("INTAKE_FORM_NOT_FOUND", "Intake form was not found.");
        }

        var version = await GetVersionAsync(form, ct);
        if (publicAccess)
        {
            if (version.Definition.AccessPolicy != IntakeAccessPolicies.Public)
            {
                throw new NotFoundException("INTAKE_FORM_NOT_FOUND", "Intake form was not found.");
            }
        }
        else
        {
            _ = await permissions.EnsureCanAsync(
                RequireUser(),
                form.ProjectId,
                PermissionCatalog.WorkItemCreate,
                ct);
            if (version.Definition.AccessPolicy != IntakeAccessPolicies.Internal)
            {
                throw new ConflictException(
                    "INTAKE_FORM_PUBLIC_ONLY",
                    "Public forms must be submitted through the public intake route.");
            }
        }

        return version;
    }

    private async Task<IntakeFormDocument> GetManagedAsync(
        string formId,
        string permission,
        CancellationToken ct)
    {
        var form = await forms.SelectAsync(x => x.Id == formId, ct)
            ?? throw new NotFoundException("INTAKE_FORM_NOT_FOUND", "Intake form was not found.");
        var authorization = await permissions.EnsureCanAsync(
            RequireUser(),
            form.ProjectId,
            permission,
            ct);
        if (authorization.OrganizationId != form.OrganizationId)
        {
            throw new NotFoundException("INTAKE_FORM_NOT_FOUND", "Intake form was not found.");
        }

        return form;
    }

    private async Task<IntakeFormVersionDocument> GetVersionAsync(
        IntakeFormDocument form,
        CancellationToken ct) =>
        await versions.SelectAsync(
            x => x.Id == IntakeStableIds.FormVersionId(form.Id, form.PublishedVersion)
                && x.OrganizationId == form.OrganizationId
                && x.FormId == form.Id,
            ct)
        ?? throw new ConflictException(
            "INTAKE_FORM_VERSION_MISSING",
            "The published intake form version is unavailable.");

    private async Task ReplaceAsync(IntakeFormDocument form, CancellationToken ct)
    {
        var result = await forms.ReplaceByVersionAsync(
            x => x.Id == form.Id && x.OrganizationId == form.OrganizationId,
            form,
            expectedVersion.Consume(form.Version),
            ct);
        if (!result.Found)
        {
            throw new NotFoundException("INTAKE_FORM_NOT_FOUND", "Intake form was not found.");
        }

        form.Version = result.Version!.Value;
    }

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

    private IntakeFormDefinitionDocument NormalizeDefinition(IntakeFormDefinitionRequest request)
    {
        if (request is null)
        {
            throw new ValidationException("Form definition is required.");
        }

        var accessPolicy = request.AccessPolicy?.Trim() switch
        {
            IntakeAccessPolicies.Internal => IntakeAccessPolicies.Internal,
            IntakeAccessPolicies.Public => IntakeAccessPolicies.Public,
            _ => throw new ValidationException("Access policy must be Internal or Public.")
        };
        var requestedFields = request.Fields?.ToList() ?? [];
        if (requestedFields.Count is < 1 || requestedFields.Count > 40
            || requestedFields.Count > options.MaxFields)
        {
            throw new ValidationException(
                $"Intake forms require between 1 and {Math.Min(40, options.MaxFields)} fields.");
        }

        var fields = requestedFields.Select(NormalizeField).ToList();
        if (fields.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() != fields.Count)
        {
            throw new ValidationException("Intake field keys must be unique.");
        }

        var mappingRequest = request.Mapping
            ?? throw new ValidationException("Intake field mapping is required.");
        var mapping = new IntakeFieldMappingDocument
        {
            TitleFieldKey = Required(mappingRequest.TitleFieldKey, "Title field key", 40),
            DescriptionFieldKey = OptionalKey(mappingRequest.DescriptionFieldKey),
            PriorityFieldKey = OptionalKey(mappingRequest.PriorityFieldKey),
            DueDateFieldKey = OptionalKey(mappingRequest.DueDateFieldKey),
            CustomFields = (mappingRequest.CustomFields ?? [])
                .Select(x => new IntakeCustomFieldMappingDocument
                {
                    IntakeFieldKey = Required(x.IntakeFieldKey, "Intake field key", 40),
                    WorkItemFieldKey = Required(x.WorkItemFieldKey, "Work item field key", 40)
                })
                .ToList()
        };
        var byKey = fields.ToDictionary(x => x.Key, StringComparer.Ordinal);
        EnsureFieldType(byKey, mapping.TitleFieldKey, "title", IntakeFieldTypes.Text, IntakeFieldTypes.LongText);
        if (!byKey[mapping.TitleFieldKey].Required)
        {
            throw new ValidationException("The title-mapped intake field must be required.");
        }
        if (mapping.DescriptionFieldKey is not null)
            EnsureFieldType(byKey, mapping.DescriptionFieldKey, "description", IntakeFieldTypes.Text, IntakeFieldTypes.LongText);
        if (mapping.PriorityFieldKey is not null)
            EnsureFieldType(byKey, mapping.PriorityFieldKey, "priority", IntakeFieldTypes.Text, IntakeFieldTypes.Choice);
        if (mapping.DueDateFieldKey is not null)
            EnsureFieldType(byKey, mapping.DueDateFieldKey, "due date", IntakeFieldTypes.Date);
        if (mapping.CustomFields.Select(x => x.IntakeFieldKey).Distinct(StringComparer.Ordinal).Count()
            != mapping.CustomFields.Count
            || mapping.CustomFields.Select(x => x.WorkItemFieldKey).Distinct(StringComparer.Ordinal).Count()
            != mapping.CustomFields.Count)
        {
            throw new ValidationException("Custom field mappings must be one-to-one.");
        }
        foreach (var custom in mapping.CustomFields)
        {
            if (!byKey.ContainsKey(custom.IntakeFieldKey))
            {
                throw new ValidationException(
                    $"Mapped intake field '{custom.IntakeFieldKey}' was not found.");
            }
            if (byKey[custom.IntakeFieldKey].Type == IntakeFieldTypes.Attachment)
            {
                throw new ValidationException(
                    "Attachment fields cannot map to work item custom fields.");
            }
        }

        return new IntakeFormDefinitionDocument
        {
            AccessPolicy = accessPolicy,
            BoardId = Required(request.BoardId, "Board id", 128),
            WorkItemType = Required(request.WorkItemType, "Work item type", 80),
            DefaultPriority = Required(request.DefaultPriority, "Default priority", 40),
            ConfirmationMessage = Required(request.ConfirmationMessage, "Confirmation message", 500),
            Fields = fields,
            Mapping = mapping
        };
    }

    private IntakeFieldDefinitionDocument NormalizeField(IntakeFieldDefinitionRequest request)
    {
        var key = Required(request.Key, "Field key", 40).ToLowerInvariant();
        if (!KeyPattern.IsMatch(key))
        {
            throw new ValidationException(
                "Field keys must start with a letter and contain only lowercase letters, numbers, underscores or hyphens.");
        }

        var type = request.Type?.Trim() switch
        {
            IntakeFieldTypes.Text => IntakeFieldTypes.Text,
            IntakeFieldTypes.LongText => IntakeFieldTypes.LongText,
            IntakeFieldTypes.Email => IntakeFieldTypes.Email,
            IntakeFieldTypes.Number => IntakeFieldTypes.Number,
            IntakeFieldTypes.Date => IntakeFieldTypes.Date,
            IntakeFieldTypes.Choice => IntakeFieldTypes.Choice,
            IntakeFieldTypes.Checkbox => IntakeFieldTypes.Checkbox,
            IntakeFieldTypes.Attachment => IntakeFieldTypes.Attachment,
            _ => throw new ValidationException(
                "Field type must be Text, LongText, Email, Number, Date, Choice, Checkbox or Attachment.")
        };
        var fieldOptions = (request.Options ?? [])
            .Select(x => Required(x, "Choice option", 120))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (type == IntakeFieldTypes.Choice && fieldOptions.Count is < 1 or > 50)
        {
            throw new ValidationException("Choice fields require between 1 and 50 unique options.");
        }
        if (type != IntakeFieldTypes.Choice && fieldOptions.Count > 0)
        {
            throw new ValidationException("Only choice fields can define options.");
        }

        return new IntakeFieldDefinitionDocument
        {
            Key = key,
            Label = Required(request.Label, "Field label", 120),
            Type = type,
            Required = request.Required,
            HelpText = Optional(request.HelpText, 500),
            Options = fieldOptions
        };
    }

    private static void EnsureFieldType(
        IReadOnlyDictionary<string, IntakeFieldDefinitionDocument> fields,
        string key,
        string target,
        params string[] supportedTypes)
    {
        if (!fields.TryGetValue(key, out var field)
            || !supportedTypes.Contains(field.Type, StringComparer.Ordinal))
        {
            throw new ValidationException(
                $"The {target} mapping must reference a compatible intake field.");
        }
    }

    private static void EnsureNotArchived(IntakeFormDocument form)
    {
        if (form.State == IntakeFormStates.Archived)
        {
            throw new ConflictException(
                "INTAKE_FORM_ARCHIVED",
                "Archived intake forms cannot be changed.");
        }
    }

    private static string? NormalizeOptionalSubmissionState(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim() switch
        {
            IntakeSubmissionStates.Processing => IntakeSubmissionStates.Processing,
            IntakeSubmissionStates.New => IntakeSubmissionStates.New,
            IntakeSubmissionStates.InReview => IntakeSubmissionStates.InReview,
            IntakeSubmissionStates.Resolved => IntakeSubmissionStates.Resolved,
            IntakeSubmissionStates.Rejected => IntakeSubmissionStates.Rejected,
            _ => throw new ValidationException("Unknown intake submission state.")
        };
    }

    private static string NormalizeTriageState(string value) => value?.Trim() switch
    {
        IntakeSubmissionStates.New => IntakeSubmissionStates.New,
        IntakeSubmissionStates.InReview => IntakeSubmissionStates.InReview,
        IntakeSubmissionStates.Resolved => IntakeSubmissionStates.Resolved,
        IntakeSubmissionStates.Rejected => IntakeSubmissionStates.Rejected,
        _ => throw new ValidationException(
            "Triage state must be New, InReview, Resolved or Rejected.")
    };

    private string RequireUser() =>
        currentUser.UserId ?? throw new UnauthorizedException("Authenticated user is required.");

    private static string Required(string? value, string field, int maxLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > maxLength)
        {
            throw new ValidationException($"{field} is required and cannot exceed {maxLength} characters.");
        }

        return normalized;
    }

    private static string Optional(string? value, int maxLength)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length > maxLength)
        {
            throw new ValidationException($"Value cannot exceed {maxLength} characters.");
        }

        return normalized;
    }

    private static string? OptionalKey(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : Required(value, "Field key", 40).ToLowerInvariant();

    internal static IntakeFormDefinitionDocument CloneDefinition(
        IntakeFormDefinitionDocument source) =>
        JsonSerializer.Deserialize<IntakeFormDefinitionDocument>(
            JsonSerializer.Serialize(source))
        ?? throw new InvalidOperationException("Intake form definition could not be cloned.");

    internal static IntakeFormResponse ToResponse(IntakeFormDocument source) => new(
        source.Id,
        source.ProjectId,
        source.Name,
        source.Description,
        source.State,
        source.State == IntakeFormStates.Published
            && source.PublishedAccessPolicy == IntakeAccessPolicies.Public
                ? source.PublicId
                : null,
        source.PublishedVersion,
        ToDefinitionResponse(source.Draft),
        source.CreatedAt,
        source.UpdatedAt,
        source.PublishedAt,
        source.Version);

    internal static PublishedIntakeFormResponse ToPublishedResponse(
        IntakeFormVersionDocument source) => new(
        source.FormId,
        source.DefinitionVersion,
        source.Name,
        source.Description,
        source.Definition.AccessPolicy,
        source.Definition.ConfirmationMessage,
        source.Definition.Fields.Select(ToFieldResponse).ToList());

    internal static IntakeSubmissionResponse ToSubmissionResponse(
        IntakeSubmissionDocument source) => new(
        source.Id,
        source.FormId,
        source.FormVersion,
        source.ProjectId,
        source.State,
        source.ConfirmationCode,
        source.WorkItemId,
        source.Values.Select(x => new IntakeSubmissionValueDocument
        {
            FieldKey = x.FieldKey,
            Value = x.Value
        }).ToList(),
        source.Attachments.Select(x => new IntakeSubmissionAttachmentResponse(
            x.Id,
            x.FieldKey,
            x.FileName,
            x.ContentType,
            x.SizeBytes,
            x.SecurityState)).ToList(),
        source.TriageNote,
        source.TriagedByUserId,
        source.CreatedAt,
        source.UpdatedAt,
        source.Version);

    private static IntakeFormDefinitionResponse ToDefinitionResponse(
        IntakeFormDefinitionDocument source) => new(
        source.AccessPolicy,
        source.BoardId,
        source.WorkItemType,
        source.DefaultPriority,
        source.ConfirmationMessage,
        source.Fields.Select(ToFieldResponse).ToList(),
        new IntakeFieldMappingResponse(
            source.Mapping.TitleFieldKey,
            source.Mapping.DescriptionFieldKey,
            source.Mapping.PriorityFieldKey,
            source.Mapping.DueDateFieldKey,
            source.Mapping.CustomFields.Select(x => new IntakeCustomFieldMappingResponse(
                x.IntakeFieldKey,
                x.WorkItemFieldKey)).ToList()));

    private static IntakeFieldDefinitionResponse ToFieldResponse(
        IntakeFieldDefinitionDocument source) => new(
        source.Key,
        source.Label,
        source.Type,
        source.Required,
        source.HelpText,
        source.Options.ToList());
}

public sealed class IntakeSubmissionService(
    IDocumentRepository<IntakeSubmissionDocument> submissions,
    IntakeFormService forms,
    IIntakeRoutePolicy routePolicy,
    IIntakeWorkItemCreator workItemCreator,
    IAttachmentStorage attachmentStorage,
    IWorkItemAuditPublisher audit,
    IClock clock,
    ICurrentUser currentUser,
    IOptions<IntakeOptions>? configuredOptions = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IntakeOptions options = configuredOptions?.Value ?? new IntakeOptions();

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
                await DeleteStoredAsync(stored, CancellationToken.None);
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
            var persisted = await submissions.ExistsByFilterAsync(
                x => x.Id == submissionId
                    && x.OrganizationId == version.OrganizationId
                    && x.FormId == version.FormId,
                CancellationToken.None);
            if (!persisted)
            {
                await DeleteStoredAsync(stored, CancellationToken.None);
            }
            throw;
        }
    }

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

    private List<IntakeSubmissionValueDocument> NormalizeValues(
        IntakeFormDefinitionDocument definition,
        IReadOnlyCollection<IntakeSubmissionValueRequest>? requestedValues)
    {
        var requests = requestedValues?.ToList() ?? [];
        if (requests.Count > options.MaxValues || requests.Count > definition.Fields.Count)
        {
            throw new ValidationException("Submission contains too many values.");
        }

        var duplicate = requests
            .Select(x => RequiredKey(x.FieldKey))
            .GroupBy(x => x, StringComparer.Ordinal)
            .FirstOrDefault(x => x.Count() > 1);
        if (duplicate is not null)
        {
            throw new ValidationException($"Submission field '{duplicate.Key}' is duplicated.");
        }

        var byKey = requests.ToDictionary(
            x => RequiredKey(x.FieldKey),
            x => x.Value ?? string.Empty,
            StringComparer.Ordinal);
        var unknown = byKey.Keys.FirstOrDefault(
            key => definition.Fields.All(field => field.Key != key));
        if (unknown is not null)
        {
            throw new ValidationException($"Submission field '{unknown}' is not defined.");
        }

        var result = new List<IntakeSubmissionValueDocument>();
        var totalCharacters = 0;
        foreach (var field in definition.Fields.Where(x => x.Type != IntakeFieldTypes.Attachment))
        {
            byKey.TryGetValue(field.Key, out var raw);
            var normalized = NormalizeValue(field, raw ?? string.Empty);
            if (field.Required && string.IsNullOrWhiteSpace(normalized))
            {
                throw new ValidationException($"Field '{field.Label}' is required.");
            }
            if (normalized.Length > options.MaxValueCharacters)
            {
                throw new ValidationException(
                    $"Field '{field.Label}' cannot exceed {options.MaxValueCharacters} characters.");
            }
            totalCharacters += normalized.Length;
            if (totalCharacters > options.MaxTotalValueCharacters)
            {
                throw new ValidationException("Submission values exceed the total size limit.");
            }
            if (normalized.Length > 0 || field.Type == IntakeFieldTypes.Checkbox)
            {
                result.Add(new IntakeSubmissionValueDocument
                {
                    FieldKey = field.Key,
                    Value = normalized
                });
            }
        }
        return result;
    }

    private static string NormalizeValue(IntakeFieldDefinitionDocument field, string value)
    {
        var normalized = value.Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        return field.Type switch
        {
            IntakeFieldTypes.Text or IntakeFieldTypes.LongText => normalized,
            IntakeFieldTypes.Email => NormalizeEmail(normalized),
            IntakeFieldTypes.Number => decimal.TryParse(
                normalized,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var number)
                    ? number.ToString(CultureInfo.InvariantCulture)
                    : throw new ValidationException($"Field '{field.Label}' requires a number."),
            IntakeFieldTypes.Date => DateOnly.TryParseExact(
                normalized,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date)
                    ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : throw new ValidationException($"Field '{field.Label}' requires an ISO date."),
            IntakeFieldTypes.Choice => field.Options.FirstOrDefault(
                option => option.Equals(normalized, StringComparison.OrdinalIgnoreCase))
                ?? throw new ValidationException($"Field '{field.Label}' contains an unknown option."),
            IntakeFieldTypes.Checkbox => bool.TryParse(normalized, out var selected)
                ? selected.ToString().ToLowerInvariant()
                : throw new ValidationException($"Field '{field.Label}' requires true or false."),
            _ => throw new ValidationException($"Field '{field.Label}' cannot contain a text value.")
        };
    }

    private void ValidateAttachmentShape(
        IntakeFormDefinitionDocument definition,
        IReadOnlyCollection<IntakeSubmissionValueDocument> values,
        IReadOnlyCollection<IntakeAttachmentUpload> attachments)
    {
        if (attachments.Count > options.MaxAttachments)
        {
            throw new ValidationException(
                $"A submission cannot contain more than {options.MaxAttachments} attachments.");
        }
        if (attachments.Sum(x => x.SizeBytes) > options.MaxTotalAttachmentBytes)
        {
            throw new ValidationException("Submission attachments exceed the total size limit.");
        }

        var attachmentFields = definition.Fields
            .Where(x => x.Type == IntakeFieldTypes.Attachment)
            .ToDictionary(x => x.Key, StringComparer.Ordinal);
        foreach (var attachment in attachments)
        {
            var key = RequiredKey(attachment.FieldKey);
            if (!attachmentFields.ContainsKey(key))
            {
                throw new ValidationException(
                    $"Attachment field '{key}' is not defined.");
            }
            if (attachment.SizeBytes is <= 0 || attachment.SizeBytes > options.MaxAttachmentBytes)
            {
                throw new ValidationException(
                    $"Each attachment must contain between 1 and {options.MaxAttachmentBytes} bytes.");
            }
        }
        foreach (var field in attachmentFields.Values.Where(x => x.Required))
        {
            if (attachments.All(x => RequiredKey(x.FieldKey) != field.Key))
            {
                throw new ValidationException($"Field '{field.Label}' requires an attachment.");
            }
        }
    }

    private async Task<IReadOnlyCollection<string>> FingerprintAttachmentsAsync(
        IReadOnlyCollection<IntakeAttachmentUpload> attachments,
        CancellationToken ct)
    {
        var result = new List<string>();
        foreach (var attachment in attachments)
        {
            if (!attachment.Content.CanSeek)
            {
                throw new ValidationException("Attachment content must support bounded replay.");
            }
            var originalPosition = attachment.Content.Position;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[64 * 1024];
            long total = 0;
            while (true)
            {
                var read = await attachment.Content.ReadAsync(buffer, ct);
                if (read == 0)
                {
                    break;
                }
                total += read;
                if (total > options.MaxAttachmentBytes)
                {
                    throw new ValidationException("Attachment content exceeds the size limit.");
                }
                hash.AppendData(buffer, 0, read);
            }
            attachment.Content.Position = originalPosition;
            if (total != attachment.SizeBytes)
            {
                throw new ValidationException("Attachment size does not match its content.");
            }
            result.Add(string.Join(
                "\u001f",
                RequiredKey(attachment.FieldKey),
                attachment.FileName,
                attachment.ContentType,
                total.ToString(CultureInfo.InvariantCulture),
                Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant()));
        }
        return result;
    }

    private static MappedWorkItem MapWorkItem(
        IntakeFormDefinitionDocument definition,
        IReadOnlyCollection<IntakeSubmissionValueDocument> values)
    {
        var byKey = values.ToDictionary(x => x.FieldKey, x => x.Value, StringComparer.Ordinal);
        var title = GetMapped(byKey, definition.Mapping.TitleFieldKey);
        var description = GetMapped(byKey, definition.Mapping.DescriptionFieldKey);
        var priority = GetMapped(byKey, definition.Mapping.PriorityFieldKey);
        var dueDateValue = GetMapped(byKey, definition.Mapping.DueDateFieldKey);
        DateTimeOffset? dueDate = dueDateValue.Length == 0
            ? null
            : new DateTimeOffset(
                DateOnly.ParseExact(
                    dueDateValue,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture).ToDateTime(TimeOnly.MinValue),
                TimeSpan.Zero);
        var fields = definition.Fields.ToDictionary(x => x.Key, StringComparer.Ordinal);
        var customFields = definition.Mapping.CustomFields
            .Where(mapping => byKey.ContainsKey(mapping.IntakeFieldKey))
            .Select(mapping => ToCustomField(
                fields[mapping.IntakeFieldKey],
                mapping.WorkItemFieldKey,
                byKey[mapping.IntakeFieldKey]))
            .ToList();
        return new MappedWorkItem(
            new CreateWorkItemRequest(
                string.Empty,
                definition.BoardId,
                title,
                definition.WorkItemType,
                priority.Length == 0 ? definition.DefaultPriority : priority,
                null,
                dueDate,
                CustomFields: customFields),
            description);
    }

    private static WorkItemCustomFieldValueRequest ToCustomField(
        IntakeFieldDefinitionDocument field,
        string workItemFieldKey,
        string value) => field.Type switch
    {
        IntakeFieldTypes.Number => new(
            workItemFieldKey,
            NumberValue: decimal.Parse(value, CultureInfo.InvariantCulture)),
        IntakeFieldTypes.Checkbox => new(
            workItemFieldKey,
            BooleanValue: bool.Parse(value)),
        IntakeFieldTypes.Date => new(
            workItemFieldKey,
            DateValue: DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture)),
        IntakeFieldTypes.Choice => new(workItemFieldKey, OptionKey: value),
        _ => new(workItemFieldKey, TextValue: value)
    };

    private static string GetMapped(
        IReadOnlyDictionary<string, string> values,
        string? key) =>
        key is not null && values.TryGetValue(key, out var value) ? value : string.Empty;

    private static string NormalizeEmail(string value)
    {
        try
        {
            var address = new MailAddress(value);
            if (!address.Address.Equals(value, StringComparison.OrdinalIgnoreCase))
            {
                throw new FormatException();
            }
            return address.Address.ToLowerInvariant();
        }
        catch (FormatException)
        {
            throw new ValidationException("Email field contains an invalid address.");
        }
    }

    private static string NormalizeIdempotencyKey(string value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 128)
        {
            throw new ValidationException(
                "Idempotency-Key must contain between 1 and 128 characters.");
        }
        return normalized;
    }

    private static string RequiredKey(string value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 40)
        {
            throw new ValidationException("Submission field key is invalid.");
        }
        return normalized;
    }

    private static string Fingerprint(
        IntakeFormVersionDocument version,
        IReadOnlyCollection<IntakeSubmissionValueDocument> values,
        IReadOnlyCollection<string> attachments)
    {
        var canonical = new
        {
            version.FormId,
            version.DefinitionVersion,
            Values = values.OrderBy(x => x.FieldKey, StringComparer.Ordinal),
            Attachments = attachments.OrderBy(x => x, StringComparer.Ordinal)
        };
        return IntakeStableIds.Hash(JsonSerializer.Serialize(canonical, JsonOptions));
    }

    private static void EnsureSameRequest(
        IntakeSubmissionDocument submission,
        string fingerprint)
    {
        if (submission.RequestFingerprint != fingerprint)
        {
            throw new ConflictException(
                "IDEMPOTENCY_KEY_REUSED",
                "Idempotency key was already used for a different intake submission.");
        }
    }

    private async Task DeleteStoredAsync(
        IEnumerable<IntakeSubmissionAttachmentDocument> attachments,
        CancellationToken ct)
    {
        foreach (var attachment in attachments)
        {
            await attachmentStorage.DeleteAsync(attachment.StoragePath, ct);
        }
    }

    private static IntakeSubmissionConfirmationResponse Confirmation(
        IntakeSubmissionDocument submission,
        IntakeFormVersionDocument version) => new(
        submission.Id,
        submission.ConfirmationCode,
        version.Definition.ConfirmationMessage,
        submission.State,
        version.Definition.AccessPolicy == IntakeAccessPolicies.Public
            ? null
            : submission.WorkItemId);

    private sealed record MappedWorkItem(
        CreateWorkItemRequest Request,
        string Description);
}
