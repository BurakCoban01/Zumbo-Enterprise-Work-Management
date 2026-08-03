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
}
