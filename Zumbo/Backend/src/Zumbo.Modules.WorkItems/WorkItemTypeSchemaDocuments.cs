using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public static class WorkItemFieldTypes
{
    public const string Text = "Text";
    public const string Number = "Number";
    public const string Boolean = "Boolean";
    public const string Date = "Date";
    public const string Select = "Select";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(
        [Text, Number, Boolean, Date, Select],
        StringComparer.Ordinal);
}

public static class IssueTypeHierarchyLevels
{
    public const string Epic = "Epic";
    public const string Standard = "Standard";
    public const string Subtask = "Subtask";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(
        [Epic, Standard, Subtask],
        StringComparer.Ordinal);
}

public sealed class WorkItemTypeSchemaDocument : IVersionedDocument
{
    public string Id { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public int SchemaVersion { get; set; } = 1;
    public List<IssueTypeDefinitionDocument> IssueTypes { get; set; } = [];
    public List<CustomFieldDefinitionDocument> CustomFields { get; set; } = [];
    public List<IssueTypeLayoutDocument> Layouts { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; }
}

public sealed class IssueTypeDefinitionDocument
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string HierarchyLevel { get; set; } = IssueTypeHierarchyLevels.Standard;
    public bool Active { get; set; } = true;
    public int Position { get; set; }
}

public sealed class CustomFieldDefinitionDocument
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = WorkItemFieldTypes.Text;
    public bool Required { get; set; }
    public bool Indexed { get; set; }
    public int? MaxLength { get; set; }
    public decimal? Minimum { get; set; }
    public decimal? Maximum { get; set; }
    public List<string> Options { get; set; } = [];
    public List<string> AppliesToIssueTypes { get; set; } = [];
    public int Position { get; set; }
}

public sealed class IssueTypeLayoutDocument
{
    public string IssueTypeKey { get; set; } = string.Empty;
    public List<string> FieldKeys { get; set; } = [];
}

public sealed class WorkItemCustomFieldValueDocument
{
    public string FieldKey { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? TextValue { get; set; }
    public decimal? NumberValue { get; set; }
    public bool? BooleanValue { get; set; }
    public DateTimeOffset? DateValueUtc { get; set; }
    public string? OptionKey { get; set; }
    public bool Indexed { get; set; }
    public string SearchValue { get; set; } = string.Empty;
}

public sealed record UpsertWorkItemTypeSchemaRequest(
    IReadOnlyCollection<IssueTypeDefinitionRequest> IssueTypes,
    IReadOnlyCollection<CustomFieldDefinitionRequest>? CustomFields,
    IReadOnlyCollection<IssueTypeLayoutRequest>? Layouts);

public sealed record IssueTypeDefinitionRequest(
    string Key,
    string Name,
    string? Description,
    string HierarchyLevel,
    bool Active = true,
    int Position = 0);

public sealed record CustomFieldDefinitionRequest(
    string Key,
    string Name,
    string Type,
    bool Required,
    bool Indexed,
    int? MaxLength,
    decimal? Minimum,
    decimal? Maximum,
    IReadOnlyCollection<string>? Options,
    IReadOnlyCollection<string>? AppliesToIssueTypes,
    int Position = 0);

public sealed record IssueTypeLayoutRequest(
    string IssueTypeKey,
    IReadOnlyCollection<string> FieldKeys);

public sealed record WorkItemCustomFieldValueRequest(
    string FieldKey,
    string? TextValue = null,
    decimal? NumberValue = null,
    bool? BooleanValue = null,
    DateOnly? DateValue = null,
    string? OptionKey = null);

public sealed record WorkItemCustomFieldValueResponse(
    string FieldKey,
    string Type,
    string? TextValue,
    decimal? NumberValue,
    bool? BooleanValue,
    DateOnly? DateValue,
    string? OptionKey);

public sealed record WorkItemTypeSchemaResponse(
    string ProjectId,
    int SchemaVersion,
    IReadOnlyCollection<IssueTypeDefinitionRequest> IssueTypes,
    IReadOnlyCollection<CustomFieldDefinitionRequest> CustomFields,
    IReadOnlyCollection<IssueTypeLayoutRequest> Layouts,
    long Version) : IVersionedResource;

public sealed record WorkItemFieldDistributionEntry(string Value, int Count);

public sealed record WorkItemFieldDistributionResponse(
    string ProjectId,
    string Field,
    int TotalItems,
    int MissingItems,
    IReadOnlyCollection<WorkItemFieldDistributionEntry> Values);

public sealed record ValidatedWorkItemShape(
    string IssueTypeKey,
    string HierarchyLevel,
    int SchemaVersion,
    IReadOnlyCollection<WorkItemCustomFieldValueDocument> CustomFields);

public sealed record ValidatedWorkItemSearchFilter(
    string? IssueType,
    string? CustomFieldKey,
    string? CustomFieldValue);

public interface IWorkItemTypeSchemaPolicy
{
    Task<ValidatedWorkItemShape> ValidateAsync(
        string projectId,
        string issueTypeKey,
        IReadOnlyCollection<WorkItemCustomFieldValueRequest>? values,
        CancellationToken ct);

    Task<string> HierarchyLevelAsync(string projectId, string issueTypeKey, CancellationToken ct);

    Task<ValidatedWorkItemSearchFilter> ValidateSearchFilterAsync(
        string projectId,
        string? issueTypeKey,
        string? customFieldKey,
        string? customFieldValue,
        CancellationToken ct);
}

public sealed class LegacyWorkItemTypeSchemaPolicy : IWorkItemTypeSchemaPolicy
{
    public Task<ValidatedWorkItemShape> ValidateAsync(
        string projectId,
        string issueTypeKey,
        IReadOnlyCollection<WorkItemCustomFieldValueRequest>? values,
        CancellationToken ct)
    {
        var normalized = LegacyCanonical(issueTypeKey);
        return Task.FromResult(new ValidatedWorkItemShape(
            normalized,
            LegacyHierarchy(normalized),
            1,
            []));
    }

    public Task<string> HierarchyLevelAsync(string projectId, string issueTypeKey, CancellationToken ct) =>
        Task.FromResult(LegacyHierarchy(issueTypeKey));

    public Task<ValidatedWorkItemSearchFilter> ValidateSearchFilterAsync(
        string projectId,
        string? issueTypeKey,
        string? customFieldKey,
        string? customFieldValue,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(customFieldKey))
        {
            throw new ValidationException("Custom field search requires a project work item schema.");
        }

        return Task.FromResult(new ValidatedWorkItemSearchFilter(
            string.IsNullOrWhiteSpace(issueTypeKey) ? null : LegacyCanonical(issueTypeKey),
            null,
            null));
    }

    private static string LegacyHierarchy(string issueTypeKey) => issueTypeKey.Trim() switch
    {
        "Epic" => IssueTypeHierarchyLevels.Epic,
        "Subtask" => IssueTypeHierarchyLevels.Subtask,
        _ => IssueTypeHierarchyLevels.Standard
    };

    private static string LegacyCanonical(string? issueTypeKey) => issueTypeKey?.Trim().ToLowerInvariant() switch
    {
        "epic" => "Epic",
        "story" => "Story",
        "task" => "Task",
        "bug" => "Bug",
        "subtask" or "sub-task" => "Subtask",
        null or "" => "Task",
        _ => issueTypeKey!.Trim()
    };
}
