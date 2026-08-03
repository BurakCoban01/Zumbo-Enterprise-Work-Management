using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record WorkItemCustomFieldValueRequest(
    string FieldKey,
    string? TextValue = null,
    decimal? NumberValue = null,
    bool? BooleanValue = null,
    DateOnly? DateValue = null,
    string? OptionKey = null);
