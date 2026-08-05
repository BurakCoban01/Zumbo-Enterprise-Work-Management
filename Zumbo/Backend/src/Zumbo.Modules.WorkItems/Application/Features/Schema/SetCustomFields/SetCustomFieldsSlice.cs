namespace Zumbo.Modules.WorkItems;

internal sealed class SetCustomFieldsSlice(
    CustomFieldMutationPipeline pipeline,
    IWorkItemTypeSchemaPolicy typeSchemas)
{
    internal async Task<WorkItemResponse> HandleAsync(
        SetCustomFieldsCommand command,
        CancellationToken ct)
    {
        var workItem = await pipeline.LoadForUpdateAsync(command.Id, ct);
        var shape = await typeSchemas.ValidateAsync(
            workItem.ProjectId,
            workItem.Type,
            command.Request.Values,
            ct);
        var oldValue = CustomFieldMutationPipeline.SerializeValues(workItem.CustomFields);
        workItem.Type = shape.IssueTypeKey;
        workItem.IssueTypeSchemaVersion = shape.SchemaVersion;
        workItem.CustomFields = shape.CustomFields.ToList();
        return await pipeline.PersistAsync(workItem, oldValue, command.CorrelationId, ct);
    }
}
