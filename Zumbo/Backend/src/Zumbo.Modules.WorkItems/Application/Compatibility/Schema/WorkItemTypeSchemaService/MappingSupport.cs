using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems.Application.Features.Schema;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemTypeSchemaService
{
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
}
