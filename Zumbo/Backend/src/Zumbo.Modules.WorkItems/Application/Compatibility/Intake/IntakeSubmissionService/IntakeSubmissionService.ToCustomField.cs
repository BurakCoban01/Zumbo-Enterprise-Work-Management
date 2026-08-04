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
}
