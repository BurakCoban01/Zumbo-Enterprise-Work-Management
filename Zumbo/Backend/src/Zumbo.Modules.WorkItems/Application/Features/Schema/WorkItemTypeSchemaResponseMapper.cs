namespace Zumbo.Modules.WorkItems.Application.Features.Schema;

internal static class WorkItemTypeSchemaResponseMapper
{
    internal static WorkItemTypeSchemaResponse ToResponse(WorkItemTypeSchemaDocument schema) => new(
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
