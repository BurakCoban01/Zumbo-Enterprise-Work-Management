using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemTypeSchemaService{

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
}
