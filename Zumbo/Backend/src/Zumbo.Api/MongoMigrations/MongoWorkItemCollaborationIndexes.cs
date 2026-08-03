using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public static class MongoWorkItemCollaborationIndexes
{
    public static IReadOnlyList<MongoIndexSpecification> All { get; } =
    [
        new(
            "WorkItems",
            "workitemcollaborations",
            "ux_workitem_collaboration_owner",
            Keys(("OrganizationId", 1), ("ProjectId", 1), ("WorkItemId", 1)),
            Unique: true),
        new(
            "WorkItems",
            "workitemeventactivitys",
            "ix_workitem_event_activity_owner_created",
            Keys(("OrganizationId", 1), ("ProjectId", 1), ("WorkItemId", 1), ("CreatedAt", -1), ("_id", 1))),
        new(
            "WorkItems",
            "workitemtemplates",
            "ux_workitem_templates_active_project_name_ci",
            Keys(("ProjectId", 1), ("Name", 1)),
            Unique: true,
            CaseInsensitive: true,
            PartialFilter: new BsonDocument("Archived", false)),
        new(
            "WorkItems",
            "workitemtemplates",
            "ix_workitem_templates_project_archived_name",
            Keys(("ProjectId", 1), ("Archived", 1), ("Name", 1), ("_id", 1))),
        new(
            "WorkItems",
            "workitemrecurrences",
            "ix_workitem_recurrences_due",
            Keys(("Active", 1), ("Archived", 1), ("NextRunAtUtc", 1), ("_id", 1))),
        new(
            "WorkItems",
            "workitemrecurrences",
            "ix_workitem_recurrences_project_archived_created",
            Keys(("ProjectId", 1), ("Archived", 1), ("CreatedAt", -1), ("_id", 1))),
        new(
            "WorkItems",
            "workitemrecurrenceoccurrences",
            "ux_workitem_recurrence_occurrence_schedule",
            Keys(("RecurrenceId", 1), ("ScheduledForUtc", 1)),
            Unique: true),
        new(
            "WorkItems",
            "workitemrecurrenceoccurrences",
            "ix_workitem_recurrence_occurrence_status_schedule",
            Keys(("RecurrenceId", 1), ("Status", 1), ("ScheduledForUtc", -1), ("_id", 1)))
    ];

    private static BsonDocument Keys(params (string Name, object Value)[] keys)
    {
        var document = new BsonDocument();
        foreach (var (name, value) in keys)
        {
            document.Add(name, BsonValue.Create(value));
        }
        return document;
    }
}
