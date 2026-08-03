using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemTemplateRecurrenceService{

    private sealed record NormalizedTemplate(
        string BoardId,
        string Name,
        string Title,
        string Description,
        string Type,
        int SchemaVersion,
        List<WorkItemCustomFieldValueDocument> CustomFields,
        string Priority,
        string? AssigneeUserId,
        string? TeamId,
        int? DueAfterDays,
        List<string> Labels);
}
