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

public sealed partial class IntakeFormService{

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
}
