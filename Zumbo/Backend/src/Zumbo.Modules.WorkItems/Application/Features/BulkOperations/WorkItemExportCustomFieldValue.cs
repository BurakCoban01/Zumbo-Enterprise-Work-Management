namespace Zumbo.Modules.WorkItems.Application.Features.BulkOperations;

internal sealed record WorkItemExportCustomFieldValue(
    string FieldKey,
    string Type,
    string? TextValue,
    decimal? NumberValue,
    bool? BooleanValue,
    DateTimeOffset? DateValueUtc,
    string? OptionKey,
    bool Indexed,
    string SearchValue);
