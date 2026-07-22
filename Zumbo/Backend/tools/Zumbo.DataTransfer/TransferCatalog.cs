using System.Text.Json;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Audit;
using Zumbo.Modules.Boards;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Notifications;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.Projects;
using Zumbo.Modules.Teams;
using Zumbo.Modules.WorkItems;
using Zumbo.Modules.Workflows;

namespace Zumbo.DataTransfer;

internal sealed record TransferDataset(
    string Name,
    Type DocumentType,
    string MongoCollection,
    string PostgreSqlSchema,
    string PostgreSqlTable);

internal static class TransferCatalog
{
    internal static IReadOnlyList<TransferDataset> All { get; } =
    [
        Data<UserDocument>("identity.users", "users", "identity", "users"),
        Data<RefreshSessionDocument>("identity.refresh-sessions", "refreshsessions", "identity", "refresh_sessions"),
        Data<ApiKeyDocument>("identity.api-keys", "apikeys", "identity", "api_keys"),
        Data<IdentityRoleDocument>("identity.roles", "identityroles", "identity", "identity_roles"),
        Data<OrganizationDocument>("organizations.organizations", "organizations", "organizations", "organizations"),
        Data<TeamDocument>("teams.teams", "teams", "teams", "teams"),
        Data<ProjectDocument>("projects.projects", "projects", "projects", "projects"),
        Data<BoardDocument>("boards.boards", "boards", "boards", "boards"),
        Data<WorkItemDocument>("work-items.work-items", "workitems", "work_items", "work_items"),
        Data<WorkItemBulkJobDocument>("work-items.bulk-jobs", "workitembulkjobs", "work_items", "work_item_bulk_jobs"),
        Data<WorkItemBulkJobItemDocument>("work-items.bulk-job-items", "workitembulkjobitems", "work_items", "work_item_bulk_job_items"),
        Data<WorkItemCommentActivityDocument>("work-items.comments", "workitemcommentactivitys", "work_items", "work_item_comments"),
        Data<WorkItemCommentRevisionActivityDocument>("work-items.comment-revisions", "workitemcommentrevisionactivitys", "work_items", "work_item_comment_revisions"),
        Data<WorkItemAttachmentActivityDocument>("work-items.attachments", "workitemattachmentactivitys", "work_items", "work_item_attachments"),
        Data<WorkItemWorkLogActivityDocument>("work-items.work-logs", "workitemworklogactivitys", "work_items", "work_item_work_logs"),
        Data<WorkItemApprovalActivityDocument>("work-items.approvals", "workitemapprovalactivitys", "work_items", "work_item_approvals"),
        Data<WorkItemTimelineActivityDocument>("work-items.timeline", "workitemtimelineactivitys", "work_items", "work_item_timeline"),
        Data<WorkflowDefinitionDocument>("workflows.definitions", "workflowdefinitions", "workflows", "workflow_definitions"),
        Data<NotificationDocument>("notifications.notifications", "notifications", "notifications", "notifications"),
        Data<NotificationPreferenceDocument>("notifications.preferences", "notificationpreferences", "notifications", "notification_preferences"),
        Data<AuditLogDocument>("audit.logs", "auditlogs", "audit", "audit_logs")
    ];

    private static TransferDataset Data<TDocument>(
        string name,
        string mongoCollection,
        string schema,
        string table)
        where TDocument : class, IDocument =>
        new(name, typeof(TDocument), mongoCollection, schema, table);
}

internal sealed record TransferManifest(
    int SchemaVersion,
    string SourceProvider,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<TransferManifestDataset> Datasets);

internal sealed record TransferManifestDataset(
    string Name,
    string File,
    long Count,
    string Sha256);

internal static class TransferJson
{
    internal static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = false
    };

    internal static string SerializeCanonical(object document, Type type)
    {
        using var parsed = JsonDocument.Parse(JsonSerializer.Serialize(document, type, SerializerOptions));
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteCanonical(writer, parsed.RootElement);
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    internal static object Deserialize(string payload, Type type) =>
        JsonSerializer.Deserialize(payload, type, SerializerOptions)
        ?? throw new InvalidDataException($"'{type.Name}' document cannot be deserialized.");

    internal static (string Id, long Version) Identity(object document)
    {
        if (document is not IDocument identified || string.IsNullOrWhiteSpace(identified.Id))
        {
            throw new InvalidDataException("Every transferred document must have a non-empty Id.");
        }
        return (identified.Id, document is IVersionedDocument versioned ? versioned.Version : 0L);
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray()) WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }
}
