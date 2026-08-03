using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemTypeSchemaService{

    private static WorkItemCustomFieldValueRequest ToRequest(WorkItemCustomFieldValueDocument value) => new(
        value.FieldKey,
        value.TextValue,
        value.NumberValue,
        value.BooleanValue,
        value.DateValueUtc is null ? null : DateOnly.FromDateTime(value.DateValueUtc.Value.UtcDateTime),
        value.OptionKey);
}
