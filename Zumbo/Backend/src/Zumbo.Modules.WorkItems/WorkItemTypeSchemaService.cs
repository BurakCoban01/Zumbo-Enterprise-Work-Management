using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class WorkItemTypeSchemaOptions
{
    public int BatchSize { get; set; } = 100;
    public int MaxBatchesPerValidation { get; set; } = 1_000;
}

public sealed partial class WorkItemTypeSchemaService(
    IDocumentRepository<WorkItemTypeSchemaDocument> schemas,
    IDocumentRepository<WorkItemDocument> workItems,
    IProjectPermissionChecker permissionChecker,
    IWorkItemAuditPublisher audit,
    IDistributedLockProvider distributedLocks,
    IOptions<DistributedLockOptions> lockOptions,
    IOptions<WorkItemTypeSchemaOptions> configuredOptions,
    IClock clock,
    ICurrentUser currentUser,
    IExpectedVersionAccessor? expectedVersions = null) : IWorkItemTypeSchemaPolicy
{
    private int BatchSize => Math.Clamp(configuredOptions.Value.BatchSize, 1, 200);
    private int MaxBatches => Math.Clamp(configuredOptions.Value.MaxBatchesPerValidation, 1, 10_000);

    public async Task<WorkItemTypeSchemaResponse> GetAsync(string projectId, CancellationToken ct)
    {
        await EnsurePermissionAsync(projectId, PermissionCatalog.WorkItemView, ct);
        return ToResponse(await LoadOrDefaultAsync(projectId, ct));
    }

    public async Task<WorkItemTypeSchemaResponse> UpsertAsync(
        string projectId,
        UpsertWorkItemTypeSchemaRequest request,
        string correlationId,
        CancellationToken ct)
    {
        await EnsurePermissionAsync(projectId, PermissionCatalog.WorkItemUpdate, ct);
        var candidate = Normalize(projectId, request, clock.UtcNow);
        await using var projectLock = await AcquireProjectLockAsync(projectId, ct);
        var stored = await schemas.SelectAsync(schema => schema.ProjectId == projectId, ct);
        candidate.SchemaVersion = (stored?.SchemaVersion ?? 0) + 1;
        candidate.CreatedAt = stored?.CreatedAt ?? candidate.CreatedAt;
        await ValidateExistingItemsAsync(candidate, ct);

        if (stored is null)
        {
            if (expectedVersions?.ExpectedVersion is long expectedVersion && expectedVersion != 0)
            {
                throw new ConflictException(
                    "WORK_ITEM_SCHEMA_CONCURRENCY_CONFLICT",
                    "Work item type schema changed concurrently; reload and retry.");
            }
            await schemas.CreateAsync(candidate, ct);
        }
        else
        {
            candidate.Id = stored.Id;
            candidate.Version = stored.Version;
            var expectedVersion = expectedVersions?.ExpectedVersion ?? stored.Version;
            var result = await schemas.ReplaceByVersionAsync(
                schema => schema.Id == stored.Id,
                candidate,
                expectedVersion,
                ct);
            if (!result.Found)
            {
                throw new ConflictException(
                    "WORK_ITEM_SCHEMA_CONCURRENCY_CONFLICT",
                    "Work item type schema changed concurrently; reload and retry.");
            }
        }

        await audit.WriteAsync(
            "WorkItemTypeSchemaUpdated",
            "Project",
            projectId,
            stored?.SchemaVersion.ToString(CultureInfo.InvariantCulture),
            candidate.SchemaVersion.ToString(CultureInfo.InvariantCulture),
            correlationId,
            ct);
        return ToResponse(candidate);
    }

    public async Task<ValidatedWorkItemShape> ValidateAsync(
        string projectId,
        string issueTypeKey,
        IReadOnlyCollection<WorkItemCustomFieldValueRequest>? values,
        CancellationToken ct)
    {
        var schema = await LoadOrDefaultAsync(projectId, ct);
        var issueType = FindActiveIssueType(schema, issueTypeKey);
        return new ValidatedWorkItemShape(
            issueType.Key,
            issueType.HierarchyLevel,
            schema.SchemaVersion,
            ValidateValues(schema, issueType.Key, values));
    }

    public async Task<string> HierarchyLevelAsync(
        string projectId,
        string issueTypeKey,
        CancellationToken ct) =>
        FindActiveIssueType(await LoadOrDefaultAsync(projectId, ct), issueTypeKey).HierarchyLevel;

    public async Task<ValidatedWorkItemSearchFilter> ValidateSearchFilterAsync(
        string projectId,
        string? issueTypeKey,
        string? customFieldKey,
        string? customFieldValue,
        CancellationToken ct)
    {
        var schema = await LoadOrDefaultAsync(projectId, ct);
        string? normalizedIssueType = null;
        if (!string.IsNullOrWhiteSpace(issueTypeKey))
        {
            normalizedIssueType = FindActiveIssueType(schema, issueTypeKey).Key;
        }

        string? normalizedFieldKey = null;
        string? normalizedFieldValue = null;
        if (!string.IsNullOrWhiteSpace(customFieldKey))
        {
            var key = NormalizeKey(customFieldKey);
            var field = schema.CustomFields.SingleOrDefault(item =>
                item.Indexed && item.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (field is null)
            {
                throw new ValidationException($"Custom field '{key}' is not indexed in the project schema.");
            }
            normalizedFieldKey = field.Key;
            normalizedFieldValue = NormalizeSearchValue(field, customFieldValue!);
        }

        return new ValidatedWorkItemSearchFilter(
            normalizedIssueType,
            normalizedFieldKey,
            normalizedFieldValue);
    }

    public async Task<WorkItemFieldDistributionResponse> GetIssueTypeDistributionAsync(
        string projectId,
        CancellationToken ct)
    {
        await EnsurePermissionAsync(projectId, PermissionCatalog.WorkItemView, ct);
        return await BuildDistributionAsync(projectId, "Type", item => item.Type, ct);
    }

    public async Task<WorkItemFieldDistributionResponse> GetCustomFieldDistributionAsync(
        string projectId,
        string fieldKey,
        CancellationToken ct)
    {
        await EnsurePermissionAsync(projectId, PermissionCatalog.WorkItemView, ct);
        var schema = await LoadOrDefaultAsync(projectId, ct);
        var key = NormalizeKey(fieldKey);
        var field = schema.CustomFields.SingleOrDefault(item =>
                item.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            ?? throw new ValidationException($"Custom field '{key}' is not defined.");
        return await BuildDistributionAsync(
            projectId,
            field.Key,
            item => item.CustomFields.SingleOrDefault(value => value.FieldKey == field.Key)?.SearchValue,
            ct);
    }

    private async Task<WorkItemFieldDistributionResponse> BuildDistributionAsync(
        string projectId,
        string field,
        Func<WorkItemDocument, string?> selector,
        CancellationToken ct)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var total = 0;
        var missing = 0;
        string? cursor = null;
        for (var batch = 0; ; batch++)
        {
            if (batch >= MaxBatches)
            {
                throw new ConflictException(
                    "WORK_ITEM_SCHEMA_REPORT_LIMIT",
                    "Work item field report exceeded the configured batch limit.");
            }

            var page = await workItems.ListByCursorAsync(
                item => item.ProjectId == projectId && !item.Archived,
                cursor,
                BatchSize,
                ct);
            foreach (var item in page.Items)
            {
                total++;
                var value = selector(item);
                if (string.IsNullOrWhiteSpace(value))
                {
                    missing++;
                }
                else
                {
                    counts[value] = counts.GetValueOrDefault(value) + 1;
                }
            }

            cursor = page.NextCursor;
            if (cursor is null)
            {
                break;
            }
        }

        return new WorkItemFieldDistributionResponse(
            projectId,
            field,
            total,
            missing,
            counts.OrderByDescending(item => item.Value)
                .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                .Select(item => new WorkItemFieldDistributionEntry(item.Key, item.Value))
                .ToList());
    }

    private async Task ValidateExistingItemsAsync(WorkItemTypeSchemaDocument candidate, CancellationToken ct)
    {
        var batches = 0;
        string? cursor = null;
        do
        {
            if (++batches > MaxBatches)
            {
                throw new ConflictException(
                    "WORK_ITEM_SCHEMA_VALIDATION_LIMIT",
                    "Work item schema validation exceeded the configured batch limit.");
            }

            var page = await workItems.ListByCursorAsync(
                item => item.ProjectId == candidate.ProjectId && !item.Archived,
                cursor,
                BatchSize,
                ct);
            foreach (var item in page.Items)
            {
                var type = FindActiveIssueType(candidate, item.Type);
                ValidateStoredValues(candidate, type.Key, item.CustomFields);
            }

            cursor = page.NextCursor;
        }
        while (cursor is not null);
    }

    private static IReadOnlyCollection<WorkItemCustomFieldValueDocument> ValidateValues(
        WorkItemTypeSchemaDocument schema,
        string issueTypeKey,
        IReadOnlyCollection<WorkItemCustomFieldValueRequest>? requested)
    {
        var values = requested ?? [];
        if (values.Count > 100
            || values.GroupBy(value => NormalizeKey(value.FieldKey), StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() > 1))
        {
            throw new ValidationException("Custom field values must contain at most 100 unique field keys.");
        }

        var result = new List<WorkItemCustomFieldValueDocument>(values.Count);
        foreach (var value in values)
        {
            var key = NormalizeKey(value.FieldKey);
            var field = schema.CustomFields.SingleOrDefault(item =>
                    item.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                ?? throw new ValidationException($"Custom field '{key}' is not defined.");
            EnsureApplies(field, issueTypeKey);
            result.Add(NormalizeValue(field, value));
        }

        EnsureRequiredValues(schema, issueTypeKey, result.Select(item => item.FieldKey));
        return result.OrderBy(item =>
                schema.CustomFields.Single(field => field.Key == item.FieldKey).Position)
            .ThenBy(item => item.FieldKey, StringComparer.Ordinal)
            .ToList();
    }

    private static void ValidateStoredValues(
        WorkItemTypeSchemaDocument schema,
        string issueTypeKey,
        IReadOnlyCollection<WorkItemCustomFieldValueDocument> values)
    {
        foreach (var value in values)
        {
            var field = schema.CustomFields.SingleOrDefault(item => item.Key == value.FieldKey)
                ?? throw new ConflictException(
                    "WORK_ITEM_SCHEMA_EXISTING_VALUE_INVALID",
                    $"Existing custom field '{value.FieldKey}' would be removed.");
            EnsureApplies(field, issueTypeKey);
            if (field.Type != value.Type)
            {
                throw new ConflictException(
                    "WORK_ITEM_SCHEMA_EXISTING_VALUE_INVALID",
                    $"Existing custom field '{value.FieldKey}' would change type.");
            }


            try
            {
                _ = NormalizeValue(field, ToRequest(value));
            }
            catch (ValidationException exception)
            {
                throw new ConflictException(
                    "WORK_ITEM_SCHEMA_EXISTING_VALUE_INVALID",
                    $"Existing custom field '{value.FieldKey}' would violate the new rules: {exception.Message}");
            }
        }

        EnsureRequiredValues(schema, issueTypeKey, values.Select(item => item.FieldKey));
    }

    private static WorkItemCustomFieldValueRequest ToRequest(WorkItemCustomFieldValueDocument value) => new(
        value.FieldKey,
        value.TextValue,
        value.NumberValue,
        value.BooleanValue,
        value.DateValueUtc is null ? null : DateOnly.FromDateTime(value.DateValueUtc.Value.UtcDateTime),
        value.OptionKey);

    private static WorkItemCustomFieldValueDocument NormalizeValue(
        CustomFieldDefinitionDocument field,
        WorkItemCustomFieldValueRequest value)
    {
        var populated = new object?[]
        {
            value.TextValue,
            value.NumberValue,
            value.BooleanValue,
            value.DateValue,
            value.OptionKey
        }.Count(item => item is not null);
        if (populated != 1)
        {
            throw new ValidationException($"Custom field '{field.Key}' requires exactly one typed value.");
        }

        var result = new WorkItemCustomFieldValueDocument
        {
            FieldKey = field.Key,
            Type = field.Type,
            Indexed = field.Indexed
        };
        switch (field.Type)
        {
            case WorkItemFieldTypes.Text when value.TextValue is not null:
                var text = value.TextValue.Trim();
                if (text.Length == 0 || text.Length > (field.MaxLength ?? 1_000))
                {
                    throw new ValidationException($"Custom field '{field.Key}' text value is outside its length limit.");
                }
                if (text.Any(char.IsControl))
                {
                    throw new ValidationException($"Custom field '{field.Key}' text value contains control characters.");
                }
                result.TextValue = text;
                result.SearchValue = text;
                break;
            case WorkItemFieldTypes.Number when value.NumberValue is not null:
                if ((field.Minimum is not null && value.NumberValue < field.Minimum)
                    || (field.Maximum is not null && value.NumberValue > field.Maximum))
                {
                    throw new ValidationException($"Custom field '{field.Key}' number value is outside its range.");
                }
                result.NumberValue = value.NumberValue;
                result.SearchValue = value.NumberValue.Value.ToString(CultureInfo.InvariantCulture);
                break;
            case WorkItemFieldTypes.Boolean when value.BooleanValue is not null:
                result.BooleanValue = value.BooleanValue;
                result.SearchValue = value.BooleanValue.Value ? "true" : "false";
                break;
            case WorkItemFieldTypes.Date when value.DateValue is not null:
                result.DateValueUtc = new DateTimeOffset(value.DateValue.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
                result.SearchValue = value.DateValue.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                break;
            case WorkItemFieldTypes.Select when value.OptionKey is not null:
                var option = value.OptionKey.Trim();
                if (!field.Options.Contains(option, StringComparer.OrdinalIgnoreCase))
                {
                    throw new ValidationException($"Custom field '{field.Key}' option is not allowed.");
                }
                result.OptionKey = field.Options.Single(item => item.Equals(option, StringComparison.OrdinalIgnoreCase));
                result.SearchValue = result.OptionKey;
                break;
            default:
                throw new ValidationException($"Custom field '{field.Key}' value does not match type '{field.Type}'.");
        }

        return result;
    }

    private static WorkItemTypeSchemaDocument Normalize(
        string projectId,
        UpsertWorkItemTypeSchemaRequest request,
        DateTimeOffset now)
    {
        if (request.IssueTypes is null || request.IssueTypes.Count is < 1 or > 50)
        {
            throw new ValidationException("A work item schema requires between 1 and 50 issue types.");
        }

        var issueTypes = request.IssueTypes.Select(NormalizeIssueType).ToList();
        if (issueTypes.GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
        {
            throw new ValidationException("Issue type keys must be unique.");
        }

        var fields = (request.CustomFields ?? []).Select(item => NormalizeField(item, issueTypes)).ToList();
        if (fields.Count > 100
            || fields.GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
        {
            throw new ValidationException("Custom field keys must be unique and cannot exceed 100 definitions.");
        }

        var layouts = NormalizeLayouts(request.Layouts, issueTypes, fields);
        return new WorkItemTypeSchemaDocument
        {
            Id = projectId,
            ProjectId = projectId,
            IssueTypes = issueTypes.OrderBy(item => item.Position).ThenBy(item => item.Key, StringComparer.Ordinal).ToList(),
            CustomFields = fields.OrderBy(item => item.Position).ThenBy(item => item.Key, StringComparer.Ordinal).ToList(),
            Layouts = layouts,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static IssueTypeDefinitionDocument NormalizeIssueType(IssueTypeDefinitionRequest request)
    {
        var key = NormalizeKey(request.Key);
        var name = request.Name?.Trim() ?? string.Empty;
        if (!KeyPattern().IsMatch(key) || name.Length is < 1 or > 100)
        {
            throw new ValidationException("Issue type keys and names are invalid.");
        }

        var hierarchy = Canonical(IssueTypeHierarchyLevels.All, request.HierarchyLevel, "issue type hierarchy level");
        return new IssueTypeDefinitionDocument
        {
            Key = key,
            Name = name,
            Description = request.Description?.Trim() ?? string.Empty,
            HierarchyLevel = hierarchy,
            Active = request.Active,
            Position = Math.Clamp(request.Position, 0, 10_000)
        };
    }

    private static CustomFieldDefinitionDocument NormalizeField(
        CustomFieldDefinitionRequest request,
        IReadOnlyCollection<IssueTypeDefinitionDocument> issueTypes)
    {
        var key = NormalizeKey(request.Key);
        var name = request.Name?.Trim() ?? string.Empty;
        if (!KeyPattern().IsMatch(key) || name.Length is < 1 or > 100)
        {
            throw new ValidationException("Custom field keys and names are invalid.");
        }

        var type = Canonical(WorkItemFieldTypes.All, request.Type, "custom field type");
        var options = (request.Options ?? []).Select(item => item.Trim()).Where(item => item.Length > 0).ToList();
        if (options.Count > 100 || options.Distinct(StringComparer.OrdinalIgnoreCase).Count() != options.Count)
        {
            throw new ValidationException($"Custom field '{key}' options must be unique and cannot exceed 100.");
        }
        if (type == WorkItemFieldTypes.Select && options.Count == 0
            || type != WorkItemFieldTypes.Select && options.Count > 0)
        {
            throw new ValidationException($"Custom field '{key}' options do not match its type.");
        }
        if (options.Any(option => option.Length > 200 || option.Any(char.IsControl)))
        {
            throw new ValidationException($"Custom field '{key}' contains an invalid option.");
        }
        if (request.Minimum is not null && request.Maximum is not null && request.Minimum > request.Maximum)
        {
            throw new ValidationException($"Custom field '{key}' number range is invalid.");
        }
        if (type == WorkItemFieldTypes.Text && (request.MaxLength ?? 1_000) is < 1 or > 4_000)
        {
            throw new ValidationException($"Custom field '{key}' text limit must be between 1 and 4000.");
        }
        if (type == WorkItemFieldTypes.Text && request.Indexed && (request.MaxLength ?? 1_000) > 200)
        {
            throw new ValidationException($"Indexed text field '{key}' limit cannot exceed 200 characters.");
        }

        var appliesTo = (request.AppliesToIssueTypes ?? issueTypes.Select(item => item.Key).ToList())
            .Select(NormalizeKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (appliesTo.Count == 0 || appliesTo.Any(keyValue =>
                issueTypes.All(item => !item.Key.Equals(keyValue, StringComparison.OrdinalIgnoreCase))))
        {
            throw new ValidationException($"Custom field '{key}' issue type scope is invalid.");
        }

        return new CustomFieldDefinitionDocument
        {
            Key = key,
            Name = name,
            Type = type,
            Required = request.Required,
            Indexed = request.Indexed,
            MaxLength = type == WorkItemFieldTypes.Text ? request.MaxLength ?? 1_000 : null,
            Minimum = type == WorkItemFieldTypes.Number ? request.Minimum : null,
            Maximum = type == WorkItemFieldTypes.Number ? request.Maximum : null,
            Options = options,
            AppliesToIssueTypes = appliesTo,
            Position = Math.Clamp(request.Position, 0, 10_000)
        };
    }

    private static string NormalizeSearchValue(CustomFieldDefinitionDocument field, string value)
    {
        var normalized = value.Trim();
        if (normalized.Length == 0)
        {
            throw new ValidationException($"Custom field '{field.Key}' search value is required.");
        }

        return field.Type switch
        {
            WorkItemFieldTypes.Text when normalized.Length <= (field.MaxLength ?? 1_000) => normalized,
            WorkItemFieldTypes.Number when decimal.TryParse(
                normalized,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var number) => number.ToString(CultureInfo.InvariantCulture),
            WorkItemFieldTypes.Boolean when bool.TryParse(normalized, out var boolean) =>
                boolean ? "true" : "false",
            WorkItemFieldTypes.Date when DateOnly.TryParseExact(
                normalized,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            WorkItemFieldTypes.Select when field.Options.Any(option =>
                option.Equals(normalized, StringComparison.OrdinalIgnoreCase)) =>
                field.Options.Single(option => option.Equals(normalized, StringComparison.OrdinalIgnoreCase)),
            _ => throw new ValidationException(
                $"Custom field '{field.Key}' search value does not match type '{field.Type}'.")
        };
    }

    private static List<IssueTypeLayoutDocument> NormalizeLayouts(
        IReadOnlyCollection<IssueTypeLayoutRequest>? requested,
        IReadOnlyCollection<IssueTypeDefinitionDocument> issueTypes,
        IReadOnlyCollection<CustomFieldDefinitionDocument> fields)
    {
        var layouts = requested?.ToList() ?? issueTypes.Select(issueType => new IssueTypeLayoutRequest(
            issueType.Key,
            fields.Where(field => field.AppliesToIssueTypes.Contains(issueType.Key, StringComparer.OrdinalIgnoreCase))
                .OrderBy(field => field.Position)
                .Select(field => field.Key)
                .ToList())).ToList();
        if (layouts.GroupBy(item => NormalizeKey(item.IssueTypeKey), StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() > 1)
            || layouts.Count != issueTypes.Count)
        {
            throw new ValidationException("Every issue type requires exactly one layout.");
        }

        return issueTypes.Select(issueType =>
        {
            var layout = layouts.SingleOrDefault(item =>
                    NormalizeKey(item.IssueTypeKey).Equals(issueType.Key, StringComparison.OrdinalIgnoreCase))
                ?? throw new ValidationException($"Issue type '{issueType.Key}' layout is missing.");
            var keys = layout.FieldKeys.Select(NormalizeKey).ToList();
            if (keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != keys.Count
                || keys.Any(key => fields.All(field =>
                    !field.Key.Equals(key, StringComparison.OrdinalIgnoreCase)
                    || !field.AppliesToIssueTypes.Contains(issueType.Key, StringComparer.OrdinalIgnoreCase))))
            {
                throw new ValidationException($"Issue type '{issueType.Key}' layout contains an invalid field.");
            }
            return new IssueTypeLayoutDocument { IssueTypeKey = issueType.Key, FieldKeys = keys };
        }).ToList();
    }

    private static IssueTypeDefinitionDocument FindActiveIssueType(
        WorkItemTypeSchemaDocument schema,
        string? issueTypeKey)
    {
        var key = issueTypeKey?.Trim().Equals("sub-task", StringComparison.OrdinalIgnoreCase) == true
            ? "Subtask"
            : string.IsNullOrWhiteSpace(issueTypeKey) ? "Task" : issueTypeKey.Trim();
        return schema.IssueTypes.SingleOrDefault(item =>
                item.Active && item.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            ?? throw new ValidationException($"Issue type '{key}' is not active in the project schema.");
    }

    private static void EnsureApplies(CustomFieldDefinitionDocument field, string issueTypeKey)
    {
        if (!field.AppliesToIssueTypes.Contains(issueTypeKey, StringComparer.OrdinalIgnoreCase))
        {
            throw new ValidationException($"Custom field '{field.Key}' does not apply to issue type '{issueTypeKey}'.");
        }
    }

    private static void EnsureRequiredValues(
        WorkItemTypeSchemaDocument schema,
        string issueTypeKey,
        IEnumerable<string> populatedKeys)
    {
        var populated = populatedKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = schema.CustomFields.FirstOrDefault(field =>
            field.Required
            && field.AppliesToIssueTypes.Contains(issueTypeKey, StringComparer.OrdinalIgnoreCase)
            && !populated.Contains(field.Key));
        if (missing is not null)
        {
            throw new ValidationException($"Required custom field '{missing.Key}' is missing.");
        }
    }

    private async Task<WorkItemTypeSchemaDocument> LoadOrDefaultAsync(string projectId, CancellationToken ct) =>
        await schemas.SelectAsync(schema => schema.ProjectId == projectId, ct) ?? Default(projectId, clock.UtcNow);

    private static WorkItemTypeSchemaDocument Default(string projectId, DateTimeOffset now)
    {
        var issueTypes = new[]
        {
            new IssueTypeDefinitionDocument { Key = "Epic", Name = "Epic", HierarchyLevel = IssueTypeHierarchyLevels.Epic, Position = 0 },
            new IssueTypeDefinitionDocument { Key = "Story", Name = "Story", HierarchyLevel = IssueTypeHierarchyLevels.Standard, Position = 10 },
            new IssueTypeDefinitionDocument { Key = "Task", Name = "Task", HierarchyLevel = IssueTypeHierarchyLevels.Standard, Position = 20 },
            new IssueTypeDefinitionDocument { Key = "Bug", Name = "Bug", HierarchyLevel = IssueTypeHierarchyLevels.Standard, Position = 30 },
            new IssueTypeDefinitionDocument { Key = "Subtask", Name = "Subtask", HierarchyLevel = IssueTypeHierarchyLevels.Subtask, Position = 40 }
        };
        return new WorkItemTypeSchemaDocument
        {
            Id = projectId,
            ProjectId = projectId,
            SchemaVersion = 1,
            IssueTypes = issueTypes.ToList(),
            Layouts = issueTypes.Select(item => new IssueTypeLayoutDocument { IssueTypeKey = item.Key }).ToList(),
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private async Task EnsurePermissionAsync(string projectId, string permission, CancellationToken ct)
    {
        var userId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new UnauthorizedException("Authenticated user is required.");
        }
        _ = await permissionChecker.EnsureCanAsync(userId, projectId, permission, ct);
    }

    private async Task<IAsyncDisposable> AcquireProjectLockAsync(string projectId, CancellationToken ct)
    {
        var options = lockOptions.Value;
        return await distributedLocks.TryAcquireAsync(
                "work-item-schema:" + projectId,
                TimeSpan.FromSeconds(Math.Clamp(options.LeaseSeconds, 5, 300)),
                TimeSpan.FromSeconds(Math.Clamp(options.WaitSeconds, 0, 30)),
                ct)
            ?? throw new ConflictException("RESOURCE_BUSY", "The work item schema is busy; retry the operation.");
    }

    private static string NormalizeKey(string? key) => key?.Trim() ?? string.Empty;

    private static string Canonical(IReadOnlySet<string> supported, string? value, string description) =>
        supported.SingleOrDefault(item => item.Equals(value?.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? throw new ValidationException($"Unsupported {description}.");

    private static WorkItemTypeSchemaResponse ToResponse(WorkItemTypeSchemaDocument schema) => new(
        schema.ProjectId,
        schema.SchemaVersion,
        schema.IssueTypes.Select(item => new IssueTypeDefinitionRequest(
            item.Key, item.Name, item.Description, item.HierarchyLevel, item.Active, item.Position)).ToList(),
        schema.CustomFields.Select(item => new CustomFieldDefinitionRequest(
            item.Key,
            item.Name,
            item.Type,
            item.Required,
            item.Indexed,
            item.MaxLength,
            item.Minimum,
            item.Maximum,
            item.Options,
            item.AppliesToIssueTypes,
            item.Position)).ToList(),
        schema.Layouts.Select(item => new IssueTypeLayoutRequest(item.IssueTypeKey, item.FieldKeys)).ToList(),
        schema.Version);

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_-]{0,39}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();
}
