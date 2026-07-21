using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed class MongoMigrationOptions
{
    public bool DryRun { get; init; }
    public bool RunDataMigrations { get; init; }
    public int BatchSize { get; init; } = 100;
    public int MaxBatchesPerRun { get; init; } = 20;
    public string? RollbackMigrationId { get; init; }
}

public sealed record MongoIndexSpecification(
    string Module,
    string Collection,
    string Name,
    BsonDocument Keys,
    bool Unique = false,
    bool CaseInsensitive = false,
    TimeSpan? ExpireAfter = null,
    BsonDocument? PartialFilter = null);

public sealed record MongoMigrationOutcome(
    string MigrationId,
    string Status,
    long Examined = 0,
    long Changed = 0,
    long Skipped = 0);

public sealed record MongoMigrationRunReport(
    bool DryRun,
    IReadOnlyList<MongoMigrationOutcome> Outcomes)
{
    public int Applied => Outcomes.Count(x => x.Status == MongoMigrationStates.Completed);
    public int Skipped => Outcomes.Count(x => x.Status == MongoMigrationStates.Skipped);
    public int Paused => Outcomes.Count(x => x.Status == MongoMigrationStates.Paused);
}

public static class MongoMigrationStates
{
    public const string Running = "Running";
    public const string Paused = "Paused";
    public const string Completed = "Completed";
    public const string RolledBack = "RolledBack";
    public const string Busy = "Busy";
    public const string Skipped = "Skipped";
    public const string DryRun = "DryRun";
}

public sealed class MongoMigrationLedgerDocument
{
    public string Id { get; set; } = string.Empty;
    public string Checksum { get; set; } = string.Empty;
    public string State { get; set; } = MongoMigrationStates.Running;
    public BsonValue Checkpoint { get; set; } = BsonNull.Value;
    public BsonValue RollbackCheckpoint { get; set; } = BsonNull.Value;
    public long Examined { get; set; }
    public long Changed { get; set; }
    public long Skipped { get; set; }
    public string? LeaseOwner { get; set; }
    public DateTime? LeaseExpiresAt { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? RolledBackAt { get; set; }
}

public sealed class MongoRankMigrationBackupDocument
{
    public string Id { get; set; } = string.Empty;
    public string MigrationId { get; set; } = string.Empty;
    public BsonValue DocumentId { get; set; } = BsonNull.Value;
    public bool HadRank { get; set; }
    public BsonValue PreviousRank { get; set; } = BsonNull.Value;
    public long AppliedRank { get; set; }
}

public static class MongoRequiredIndexes
{
    public static IReadOnlyList<MongoIndexSpecification> All { get; } =
    [
        Index("Identity", "users", "ux_users_username_ci", new("Username", 1), unique: true, ci: true),
        Index("Identity", "users", "ux_users_email_ci", new("Email", 1), unique: true, ci: true),
        Index("Identity", "users", "ix_users_active_username", Keys(("IsActive", 1), ("Username", 1), ("_id", 1))),
        Index("Identity", "users", "ix_users_organization_active_username", Keys(("OrganizationId", 1), ("IsActive", 1), ("Username", 1), ("_id", 1))),
        Index("Identity", "users", "ix_users_refresh_token_hash", new("RefreshTokens.TokenHash", 1)),
        Index("Identity", "users", "ux_users_password_reset_token_hash", new("PasswordResetTokenHash", 1), unique: true, partial: new("PasswordResetTokenHash", new BsonDocument("$type", "string"))),
        Index("Identity", "users", "ix_users_active_roles", Keys(("IsActive", 1), ("Roles", 1), ("_id", 1))),
        Index("Identity", "identityroles", "ux_identityroles_organization_name_ci", Keys(("OrganizationId", 1), ("Name", 1)), unique: true, ci: true),
        Index("Identity", "identityroles", "ix_identityroles_system_organization_name", Keys(("IsSystem", 1), ("OrganizationId", 1), ("Name", 1), ("_id", 1))),
        Index("Identity", "apikeys", "ix_apikeys_user_created", Keys(("UserId", 1), ("CreatedAt", -1), ("_id", 1))),
        Index("Identity", "apikeys", "ttl_apikeys_expires_utc", new("ExpiresAtUtc", 1), expireAfter: TimeSpan.Zero),
        Index("Organizations", "organizations", "ux_organizations_tenant_key_ci", new("TenantKey", 1), unique: true, ci: true),
        Index("Organizations", "organizations", "ix_organizations_name", Keys(("Name", 1), ("_id", 1))),
        Index("Projects", "projects", "ux_projects_organization_key_ci", Keys(("OrganizationId", 1), ("Key", 1)), unique: true, ci: true),
        Index("Projects", "projects", "ix_projects_organization_archived_key", Keys(("OrganizationId", 1), ("Archived", 1), ("Key", 1), ("_id", 1))),
        Index("Teams", "teams", "ux_teams_organization_name_ci", Keys(("OrganizationId", 1), ("Name", 1)), unique: true, ci: true),
        Index("Teams", "teams", "ix_teams_organization_archived_name", Keys(("OrganizationId", 1), ("Archived", 1), ("Name", 1), ("_id", 1))),
        Index("Boards", "boards", "ux_boards_active_project_name_ci", Keys(("ProjectId", 1), ("Name", 1)), unique: true, ci: true, partial: new("Archived", false)),
        Index("Boards", "boards", "ix_boards_project_archived_name", Keys(("ProjectId", 1), ("Archived", 1), ("Name", 1), ("_id", 1))),
        Index("Workflows", "workflows", "ux_workflows_project", new("ProjectId", 1), unique: true),
        Index("WorkItems", "workitems", "ix_workitems_project_archived_created", Keys(("ProjectId", 1), ("Archived", 1), ("CreatedAt", -1), ("_id", 1))),
        Index("WorkItems", "workitems", "ix_workitems_board_column_archived_rank", Keys(("BoardId", 1), ("ColumnId", 1), ("Archived", 1), ("Rank", 1), ("_id", 1))),
        Index("WorkItems", "workitems", "ix_workitems_project_archived_status_rank", Keys(("ProjectId", 1), ("Archived", 1), ("Status", 1), ("Rank", 1), ("_id", 1))),
        Index("WorkItems", "workitems", "ix_workitems_project_archived_type_rank", Keys(("ProjectId", 1), ("Archived", 1), ("Type", 1), ("Rank", 1), ("_id", 1))),
        Index("WorkItems", "workitems", "ix_workitems_project_archived_custom_field", Keys(("ProjectId", 1), ("Archived", 1), ("CustomFields.FieldKey", 1), ("CustomFields.SearchValue", 1), ("_id", 1))),
        Index("WorkItems", "workitemtypeschemas", "ux_workitem_type_schemas_project", new("ProjectId", 1), unique: true),
        Index("WorkItems", "boardcolumnwipprojections", "ix_wip_projection_project_board_column", Keys(("ProjectId", 1), ("BoardId", 1), ("ColumnId", 1))),
        Index("WorkItems", "workitems", "ix_workitems_project_archived_rank", Keys(("ProjectId", 1), ("Archived", 1), ("Rank", 1), ("_id", 1))),
        Index("WorkItems", "workitems", "ix_workitems_parent_archived", Keys(("ParentId", 1), ("Archived", 1))),
        Index("WorkItems", "workitems", "ix_workitems_project_archived_assignee_rank", Keys(("ProjectId", 1), ("Archived", 1), ("AssigneeUserId", 1), ("Rank", 1), ("_id", 1))),
        Index("WorkItems", "workitems", "ix_workitems_project_archived_due", Keys(("ProjectId", 1), ("Archived", 1), ("DueDate", 1), ("_id", 1))),
        Index("WorkItems", "workitems", "ix_workitems_project_archived_sprint", Keys(("ProjectId", 1), ("Archived", 1), ("SprintId", 1))),
        Index("WorkItems", "workitems", "ix_workitems_project_archived_completed", Keys(("ProjectId", 1), ("Archived", 1), ("CompletedAt", 1))),
        Index("WorkItems", "workitems", "ix_workitems_due_reminder", Keys(("Archived", 1), ("DueDate", 1), ("_id", 1))),
        Index("WorkItems", "sprints", "ux_sprints_project_name_ci", Keys(("ProjectId", 1), ("Name", 1)), unique: true, ci: true),
        Index("WorkItems", "sprints", "ux_sprints_active_project", new("ProjectId", 1), unique: true, partial: new("Status", "Active")),
        Index("WorkItems", "sprints", "ix_sprints_project_status_start", Keys(("ProjectId", 1), ("Status", 1), ("StartAtUtc", -1), ("_id", 1))),
        Index("WorkItems", "sprintscopesnapshots", "ix_sprint_scope_sprint_item", Keys(("SprintId", 1), ("WorkItemId", 1))),
        Index("WorkItems", "sprintcompletionsnapshots", "ix_sprint_completion_sprint_item", Keys(("SprintId", 1), ("WorkItemId", 1))),
        Index("Audit", "auditlogs", "ix_auditlogs_entity_created", Keys(("EntityType", 1), ("EntityId", 1), ("CreatedAt", -1))),
        Index("Audit", "auditlogs", "ix_auditlogs_actor_created", Keys(("ActorUserId", 1), ("CreatedAt", -1))),
        Index("Audit", "auditlogs", "ix_auditlogs_action_created", Keys(("Action", 1), ("CreatedAt", -1))),
        Index("Audit", "auditlogs", "ix_auditlogs_created", Keys(("CreatedAt", -1), ("_id", 1))),
        Index("Notifications", "notifications", "ix_notifications_user_read_created", Keys(("UserId", 1), ("Read", 1), ("CreatedAt", -1), ("_id", 1))),
        Index("Notifications", "notifications", "ux_notifications_deduplication_key", new("DeduplicationKey", 1), unique: true, partial: new("DeduplicationKey", new BsonDocument("$type", "string"))),
        Index("Notifications", "notifications", "ix_notifications_email_status_next_attempt", Keys(("EmailStatus", 1), ("EmailNextAttemptAt", 1))),
        Index("Notifications", "notificationpreferences", "ux_notificationpreferences_user", new("UserId", 1), unique: true)
    ];

    private static MongoIndexSpecification Index(
        string module,
        string collection,
        string name,
        BsonDocument keys,
        bool unique = false,
        bool ci = false,
        TimeSpan? expireAfter = null,
        BsonDocument? partial = null) =>
        new(module, collection, name, keys, unique, ci, expireAfter, partial);

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

public static class MongoWorkItemReportingIndexes
{
    public static IReadOnlyList<MongoIndexSpecification> All { get; } =
    [
        new(
            "WorkItems",
            "workitems",
            "ix_workitems_project_archived_id",
            new BsonDocument
            {
                ["ProjectId"] = 1,
                ["Archived"] = 1,
                ["_id"] = 1
            })
    ];
}

public static class MongoWorkItemReportActivityIndexes
{
    public static IReadOnlyList<MongoIndexSpecification> All { get; } =
    [
        new(
            "WorkItems",
            "workitems",
            "ix_workitems_project_archived_team_created",
            new BsonDocument
            {
                ["ProjectId"] = 1,
                ["Archived"] = 1,
                ["TeamId"] = 1,
                ["CreatedAt"] = 1,
                ["_id"] = 1
            }),
        new(
            "WorkItems",
            "workitemworklogactivitys",
            "ix_workitem_worklogs_project_cursor",
            new BsonDocument
            {
                ["OrganizationId"] = 1,
                ["ProjectId"] = 1,
                ["_id"] = 1
            }),
        new(
            "WorkItems",
            "workitemtimelineactivitys",
            "ix_workitem_timeline_project_cursor",
            new BsonDocument
            {
                ["OrganizationId"] = 1,
                ["ProjectId"] = 1,
                ["_id"] = 1
            })
    ];
}

public static class MongoWorkItemGraphIndexes
{
    public static IReadOnlyList<MongoIndexSpecification> All { get; } =
    [
        new(
            "WorkItems",
            "workitemrelationedges",
            "ux_workitem_relation_edges_source",
            Keys(("ProjectId", 1), ("SourceWorkItemId", 1), ("TargetWorkItemId", 1), ("RelationType", 1)),
            Unique: true),
        new(
            "WorkItems",
            "workitemrelationedges",
            "ix_workitem_relation_edges_dependency_from",
            Keys(("ProjectId", 1), ("DependencyFromWorkItemId", 1), ("DependencyToWorkItemId", 1))),
        new(
            "WorkItems",
            "workitemrelationedges",
            "ix_workitem_relation_edges_dependency_to",
            Keys(("ProjectId", 1), ("DependencyToWorkItemId", 1), ("DependencyFromWorkItemId", 1))),
        new(
            "WorkItems",
            "workitems",
            "ix_workitems_project_parent_archived",
            Keys(("ProjectId", 1), ("ParentId", 1), ("Archived", 1), ("_id", 1)))
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

public static class MongoWorkItemBulkJobIndexes
{
    public static IReadOnlyList<MongoIndexSpecification> All { get; } =
    [
        new(
            "WorkItems",
            "workitembulkjobs",
            "ux_workitem_bulk_jobs_idempotency",
            new BsonDocument { ["OrganizationId"] = 1, ["RequestedByUserId"] = 1, ["IdempotencyKeyHash"] = 1 },
            Unique: true),
        new(
            "WorkItems",
            "workitembulkjobs",
            "ix_workitem_bulk_jobs_owner_created",
            new BsonDocument { ["OrganizationId"] = 1, ["ProjectId"] = 1, ["RequestedByUserId"] = 1, ["CreatedAt"] = -1, ["_id"] = 1 }),
        new(
            "WorkItems",
            "workitembulkjobs",
            "ix_workitem_bulk_jobs_state_updated",
            new BsonDocument { ["State"] = 1, ["UpdatedAt"] = 1, ["_id"] = 1 }),
        new(
            "WorkItems",
            "workitembulkjobitems",
            "ux_workitem_bulk_job_items_order",
            new BsonDocument { ["JobId"] = 1, ["ItemIndex"] = 1 },
            Unique: true),
        new(
            "WorkItems",
            "workitembulkjobitems",
            "ix_workitem_bulk_job_items_state_order",
            new BsonDocument { ["JobId"] = 1, ["State"] = 1, ["ItemIndex"] = 1, ["_id"] = 1 })
    ];
}

public static class MongoAuditTenantIndexes
{
    public static IReadOnlyList<MongoIndexSpecification> All { get; } =
    [
        new("Audit", "auditlogs", "ix_auditlogs_organization_created",
            new BsonDocument { ["OrganizationId"] = 1, ["CreatedAt"] = -1, ["_id"] = 1 }),
        new("Audit", "auditlogs", "ix_auditlogs_organization_entity_created",
            new BsonDocument { ["OrganizationId"] = 1, ["EntityType"] = 1, ["EntityId"] = 1, ["CreatedAt"] = -1, ["_id"] = 1 }),
        new("Audit", "auditlogs", "ix_auditlogs_organization_actor_created",
            new BsonDocument { ["OrganizationId"] = 1, ["ActorUserId"] = 1, ["CreatedAt"] = -1, ["_id"] = 1 }),
        new("Audit", "auditlogs", "ux_auditlogs_organization_chain_sequence",
            new BsonDocument { ["OrganizationId"] = 1, ["ChainSequence"] = 1 },
            Unique: true,
            PartialFilter: new BsonDocument("ChainSequence", new BsonDocument("$gt", 0)))
    ];
}

public static class MongoPrivacyWorkflowIndexes
{
    public static IReadOnlyList<MongoIndexSpecification> All { get; } =
    [
        new(
            "Identity",
            "privacyworkflows",
            "ix_privacy_workflows_owner_state",
            new BsonDocument
            {
                ["OrganizationId"] = 1,
                ["RequestedByUserId"] = 1,
                ["State"] = 1,
                ["_id"] = 1
            }),
        new(
            "Identity",
            "privacyworkflows",
            "ix_privacy_workflows_retention",
            new BsonDocument { ["ExpiresAt"] = 1, ["_id"] = 1 })
    ];
}

public static class MongoPrivacyWorkflowUtcIndexes
{
    public static IReadOnlyList<MongoIndexSpecification> All { get; } =
    [
        new(
            "Identity",
            "privacyworkflows",
            "ix_privacy_workflows_retention_utc",
            new BsonDocument { ["ExpiresAtUtc"] = 1, ["_id"] = 1 })
    ];
}

public static class MongoNotificationDeliveryIndexes
{
    public static IReadOnlyList<MongoIndexSpecification> All { get; } =
    [
        new("Notifications", "notifications", "ux_notifications_deduplication_key",
            new BsonDocument { ["OrganizationId"] = 1, ["DeduplicationKey"] = 1 },
            Unique: true,
            PartialFilter: new BsonDocument("DeduplicationKey", new BsonDocument("$type", "string"))),
        new("Notifications", "notifications", "ix_notifications_email_status_next_attempt",
            new BsonDocument
            {
                ["EmailStatus"] = 1,
                ["EmailNextAttemptAt"] = 1,
                ["EmailLeaseUntil"] = 1,
                ["OrganizationId"] = 1,
                ["_id"] = 1
            })
    ];
}

public static class MongoWebhookIndexes
{
    public static IReadOnlyList<MongoIndexSpecification> All { get; } =
    [
        new(
            "WorkItems",
            "webhooksubscriptions",
            "ix_webhook_subscriptions_tenant_active",
            new BsonDocument
            {
                ["OrganizationId"] = 1,
                ["IsActive"] = 1,
                ["_id"] = 1
            }),
        new(
            "WorkItems",
            "webhookdeliverys",
            "ix_webhook_deliveries_claim",
            new BsonDocument
            {
                ["Status"] = 1,
                ["NextAttemptAtUtc"] = 1,
                ["LeaseUntilUtc"] = 1,
                ["_id"] = 1
            }),
        new(
            "WorkItems",
            "webhookdeliverys",
            "ix_webhook_deliveries_tenant_subscription",
            new BsonDocument
            {
                ["OrganizationId"] = 1,
                ["SubscriptionId"] = 1,
                ["_id"] = 1
            }),
        new(
            "WorkItems",
            "webhookdeliverys",
            "ix_webhook_deliveries_tenant_status",
            new BsonDocument
            {
                ["OrganizationId"] = 1,
                ["Status"] = 1,
                ["_id"] = 1
            })
    ];
}

public static class MongoDurableMessagingIndexes
{
    public static IReadOnlyList<MongoIndexSpecification> All { get; } =
    [
        new(
            "WorkItems",
            "outbox_messages",
            "ux_outbox_owner_event_deduplication",
            new BsonDocument { ["OwnerModule"] = 1, ["EventType"] = 1, ["DeduplicationKey"] = 1 },
            Unique: true,
            PartialFilter: new BsonDocument("DeduplicationKey", new BsonDocument("$type", "string"))),
        new(
            "WorkItems",
            "outbox_messages",
            "ix_outbox_pending_claim",
            new BsonDocument { ["Status"] = 1, ["AvailableAtUtc"] = 1, ["OccurredAtUtc"] = 1, ["_id"] = 1 }),
        new(
            "WorkItems",
            "outbox_messages",
            "ix_outbox_expired_lease",
            new BsonDocument { ["Status"] = 1, ["LeaseUntilUtc"] = 1, ["OccurredAtUtc"] = 1, ["_id"] = 1 }),
        new(
            "WorkItems",
            "outbox_messages",
            "ix_outbox_dead_letter",
            new BsonDocument { ["Status"] = 1, ["DeadLetteredAtUtc"] = -1, ["_id"] = 1 }),
        new(
            "WorkItems",
            "inbox_messages",
            "ix_inbox_consumer_processed",
            new BsonDocument { ["ConsumerName"] = 1, ["ProcessedAtUtc"] = -1, ["_id"] = 1 }),
        new(
            "Audit",
            "auditlogs",
            "ux_auditlogs_deduplication_key",
            new BsonDocument("DeduplicationKey", 1),
            Unique: true,
            PartialFilter: new BsonDocument("DeduplicationKey", new BsonDocument("$type", "string")))
    ];
}

public static class MongoIdentityCredentialIndexes
{
    public static IReadOnlyList<MongoIndexSpecification> All { get; } =
    [
        new(
            "Identity",
            "refreshsessions",
            "ux_refreshsessions_token_hash",
            new BsonDocument("TokenHash", 1),
            Unique: true),
        new(
            "Identity",
            "refreshsessions",
            "ix_refreshsessions_owner_active",
            new BsonDocument
            {
                ["OrganizationId"] = 1,
                ["UserId"] = 1,
                ["RevokedAtUtc"] = 1,
                ["ExpiresAtUtc"] = 1,
                ["_id"] = 1
            }),
        new(
            "Identity",
            "refreshsessions",
            "ttl_refreshsessions_retain_until_utc",
            new BsonDocument("RetainUntilUtc", 1),
            ExpireAfter: TimeSpan.Zero),
        new(
            "Identity",
            "apikeys",
            "ix_apikeys_owner_created",
            new BsonDocument
            {
                ["OrganizationId"] = 1,
                ["UserId"] = 1,
                ["CreatedAt"] = -1,
                ["_id"] = 1
            }),
        new(
            "Identity",
            "apikeys",
            "ix_apikeys_owner_revoked_expires",
            new BsonDocument
            {
                ["OrganizationId"] = 1,
                ["UserId"] = 1,
                ["RevokedAtUtc"] = 1,
                ["ExpiresAtUtc"] = 1,
                ["_id"] = 1
            })
    ];
}

internal static class MongoLegacyIdentityCredentialIndexes
{
    public static IReadOnlyList<MongoIndexSpecification> All { get; } =
        MongoIdentityCredentialIndexes.All.Select(specification => specification.Name switch
        {
            "ix_refreshsessions_owner_active" => specification with
            {
                Keys = new BsonDocument
                {
                    ["OrganizationId"] = 1,
                    ["UserId"] = 1,
                    ["RevokedAt"] = 1,
                    ["ExpiresAt"] = 1,
                    ["_id"] = 1
                }
            },
            "ix_apikeys_owner_revoked_expires" => specification with
            {
                Keys = new BsonDocument
                {
                    ["OrganizationId"] = 1,
                    ["UserId"] = 1,
                    ["RevokedAt"] = 1,
                    ["ExpiresAt"] = 1,
                    ["_id"] = 1
                }
            },
            _ => specification
        }).ToList();
}

public static class MongoWorkItemActivityIndexes
{
    public static IReadOnlyList<MongoIndexSpecification> All { get; } =
    [
        ActivityIndex("workitemcommentactivitys", "ix_workitem_comments_owner_created", "CreatedAt"),
        new(
            "WorkItems",
            "workitemcommentrevisionactivitys",
            "ix_workitem_revisions_owner_comment_edited",
            new BsonDocument
            {
                ["OrganizationId"] = 1,
                ["ProjectId"] = 1,
                ["WorkItemId"] = 1,
                ["CommentId"] = 1,
                ["EditedAt"] = 1,
                ["_id"] = 1
            }),
        ActivityIndex("workitemattachmentactivitys", "ix_workitem_attachments_owner_created", "CreatedAt"),
        ActivityIndex("workitemworklogactivitys", "ix_workitem_worklogs_owner_created", "CreatedAt"),
        ActivityIndex("workitemapprovalactivitys", "ix_workitem_approvals_owner_requested", "RequestedAt"),
        ActivityIndex("workitemtimelineactivitys", "ix_workitem_timeline_owner_changed", "ChangedAt")
    ];

    private static MongoIndexSpecification ActivityIndex(
        string collection,
        string name,
        string chronologicalField) =>
        new(
            "WorkItems",
            collection,
            name,
            new BsonDocument
            {
                ["OrganizationId"] = 1,
                ["ProjectId"] = 1,
                ["WorkItemId"] = 1,
                [chronologicalField] = 1,
                ["_id"] = 1
            });
}

public sealed class MongoMigrationRunner(
    IMongoDbService mongo,
    IOptions<MongoMigrationOptions> configuredOptions,
    ILogger<MongoMigrationRunner> logger)
{
    public const string IndexMigrationId = "20260719_001_required_indexes";
    public const string RankMigrationId = "20260719_002_workitem_rank_backfill";
    public const string DurableMessagingIndexMigrationId = "20260719_003_durable_messaging_indexes";
    public const string IdentityCredentialIndexMigrationId = "20260719_004_identity_credential_indexes";
    public const string RefreshSessionMigrationId = "20260719_005_refresh_session_backfill";
    public const string ApiKeyVersionMigrationId = "20260719_006_api_key_version_backfill";
    public const string IdentityCredentialScalarUtcIndexMigrationId =
        "20260719_007_identity_credential_scalar_utc_indexes";
    public const string WorkItemActivityIndexMigrationId =
        "20260719_008_workitem_activity_indexes";
    public const string WorkItemActivityMigrationId =
        "20260719_009_workitem_activity_backfill";
    public const string OrganizationVersionMigrationId =
        "20260720_010_organization_version_backfill";
    public const string TeamInviteTokenMigrationId =
        "20260720_011_team_invite_token_backfill";
    public const string ProjectLifecycleMigrationId =
        "20260720_012_project_lifecycle_backfill";
    public const string WorkflowLifecycleMigrationId =
        "20260720_013_workflow_lifecycle_backfill";
    public const string SprintLifecycleMigrationId =
        "20260720_014_sprint_lifecycle_backfill";
    public const string WorkItemTypeSchemaMigrationId =
        "20260720_015_workitem_type_schema_backfill";
    public const string WorkItemGraphIndexMigrationId =
        "20260720_016_workitem_graph_indexes";
    public const string WorkItemGraphMigrationId =
        "20260720_017_workitem_graph_edge_backfill";
    public const string WorkItemCollaborationIndexMigrationId =
        "20260720_018_workitem_collaboration_indexes";
    public const string WorkItemBulkJobIndexMigrationId =
        "20260720_019_workitem_bulk_job_indexes";
    public const string AuditTenantIndexMigrationId =
        "20260720_020_audit_tenant_indexes";
    public const string NotificationDeliveryIndexMigrationId =
        "20260720_021_notification_delivery_indexes";
    public const string WorkItemReportingIndexMigrationId =
        "20260720_022_workitem_reporting_indexes";
    public const string WorkItemReportActivityIndexMigrationId =
        "20260720_023_workitem_report_activity_indexes";
    public const string PrivacyWorkflowIndexMigrationId =
        "20260720_024_privacy_workflow_indexes";
    public const string PrivacyWorkflowUtcIndexMigrationId =
        "20260720_025_privacy_workflow_utc_index";
    public const string WebhookIndexMigrationId =
        "20260720_026_webhook_indexes";

    private const string LedgerCollection = "__zumbo_migrations";
    private const string BackupCollection = "__zumbo_migration_rank_backups";
    private const string ControlModule = "Default";
    private const string WorkItemsModule = "WorkItems";
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);

    private readonly MongoMigrationOptions _options = configuredOptions.Value;
    private readonly string _owner = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    public async Task<MongoMigrationRunReport> RunAsync(CancellationToken cancellationToken = default)
    {
        ValidateOptions();
        if (!string.IsNullOrWhiteSpace(_options.RollbackMigrationId))
        {
            var rollback = await RollbackAsync(_options.RollbackMigrationId.Trim(), cancellationToken);
            return new MongoMigrationRunReport(_options.DryRun, [rollback]);
        }

        var outcomes = new List<MongoMigrationOutcome>
        {
            await ApplyIndexesAsync(IndexMigrationId, MongoRequiredIndexes.All, cancellationToken),
            await ApplyIndexesAsync(
                DurableMessagingIndexMigrationId,
                MongoDurableMessagingIndexes.All,
                cancellationToken),
            await ApplyIndexesAsync(
                IdentityCredentialIndexMigrationId,
                MongoLegacyIdentityCredentialIndexes.All,
                cancellationToken),
            await ReplaceIdentityCredentialScalarUtcIndexesAsync(cancellationToken),
            await ApplyIndexesAsync(
                WorkItemActivityIndexMigrationId,
                MongoWorkItemActivityIndexes.All,
                cancellationToken),
            await ApplyIndexesAsync(
                WorkItemGraphIndexMigrationId,
                MongoWorkItemGraphIndexes.All,
                cancellationToken),
            await ApplyIndexesAsync(
                WorkItemCollaborationIndexMigrationId,
                MongoWorkItemCollaborationIndexes.All,
                cancellationToken),
            await ApplyIndexesAsync(
                WorkItemBulkJobIndexMigrationId,
                MongoWorkItemBulkJobIndexes.All,
                cancellationToken),
            await ApplyIndexesAsync(
                AuditTenantIndexMigrationId,
                MongoAuditTenantIndexes.All,
                cancellationToken),
            await ReplaceNotificationDeliveryIndexesAsync(cancellationToken),
            await ApplyIndexesAsync(
                WorkItemReportingIndexMigrationId,
                MongoWorkItemReportingIndexes.All,
                cancellationToken),
            await ApplyIndexesAsync(
                WorkItemReportActivityIndexMigrationId,
                MongoWorkItemReportActivityIndexes.All,
                cancellationToken),
            await ApplyIndexesAsync(
                PrivacyWorkflowIndexMigrationId,
                MongoPrivacyWorkflowIndexes.All,
                cancellationToken),
            await ApplyIndexesAsync(
                PrivacyWorkflowUtcIndexMigrationId,
                MongoPrivacyWorkflowUtcIndexes.All,
                cancellationToken),
            await ApplyIndexesAsync(
                WebhookIndexMigrationId,
                MongoWebhookIndexes.All,
                cancellationToken)
        };
        if (_options.RunDataMigrations)
        {
            outcomes.Add(await BackfillRanksAsync(cancellationToken));
            outcomes.Add(await BackfillRefreshSessionsAsync(cancellationToken));
            outcomes.Add(await BackfillApiKeyVersionsAsync(cancellationToken));
            outcomes.Add(await BackfillWorkItemActivitiesAsync(cancellationToken));
            outcomes.Add(await BackfillOrganizationVersionsAsync(cancellationToken));
            outcomes.Add(await ExpireLegacyTeamInvitesAsync(cancellationToken));
            outcomes.Add(await BackfillProjectLifecycleAsync(cancellationToken));
            outcomes.Add(await BackfillWorkflowLifecycleAsync(cancellationToken));
            outcomes.Add(await BackfillSprintLifecycleAsync(cancellationToken));
            outcomes.Add(await BackfillWorkItemTypeSchemasAsync(cancellationToken));
            outcomes.Add(await BackfillWorkItemGraphAsync(cancellationToken));
        }

        return new MongoMigrationRunReport(_options.DryRun, outcomes);
    }

    public async Task<MongoMigrationOutcome> RollbackAsync(
        string migrationId,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(migrationId, RankMigrationId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Migration '{migrationId}' does not support rollback.");
        }

        if (_options.DryRun)
        {
            return new MongoMigrationOutcome(migrationId, MongoMigrationStates.DryRun);
        }

        var ledger = await LoadLedgerAsync(migrationId, cancellationToken)
            ?? throw new InvalidOperationException($"Migration '{migrationId}' has not been applied.");
        EnsureChecksum(ledger, RankChecksum);
        if (ledger.State == MongoMigrationStates.RolledBack)
        {
            return ToOutcome(ledger, MongoMigrationStates.Skipped);
        }

        ledger = await AcquireLeaseAsync(ledger, cancellationToken);
        if (ledger.LeaseOwner != _owner)
        {
            return ToOutcome(ledger, MongoMigrationStates.Busy);
        }

        var backups = mongo.GetCollection<MongoRankMigrationBackupDocument>(BackupCollection, WorkItemsModule);
        var workItems = mongo.GetCollection<BsonDocument>("workitems", WorkItemsModule);
        var changed = 0L;
        var skipped = 0L;
        while (true)
        {
            var filter = Builders<MongoRankMigrationBackupDocument>.Filter.Eq(x => x.MigrationId, migrationId);
            if (!ledger.RollbackCheckpoint.IsBsonNull)
            {
                filter &= Builders<MongoRankMigrationBackupDocument>.Filter.Gt(x => x.DocumentId, ledger.RollbackCheckpoint);
            }

            var batch = await backups.Find(filter)
                .SortBy(x => x.DocumentId)
                .Limit(BatchSize)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                break;
            }

            foreach (var backup in batch)
            {
                var currentFilter = new BsonDocument
                {
                    ["_id"] = backup.DocumentId,
                    ["Rank"] = backup.AppliedRank
                };
                var rankUpdate = backup.HadRank
                    ? new BsonDocument("$set", new BsonDocument("Rank", backup.PreviousRank))
                    : new BsonDocument("$unset", new BsonDocument("Rank", string.Empty));
                var result = await workItems.UpdateOneAsync(currentFilter, rankUpdate, cancellationToken: cancellationToken);
                if (result.ModifiedCount == 1) changed++; else skipped++;
            }

            ledger.RollbackCheckpoint = batch[^1].DocumentId;
            ledger.Changed = changed;
            ledger.Skipped = skipped;
            await SaveOwnedLedgerAsync(ledger, cancellationToken);
        }

        ledger.State = MongoMigrationStates.RolledBack;
        ledger.Checkpoint = BsonNull.Value;
        ledger.RollbackCheckpoint = BsonNull.Value;
        ledger.RolledBackAt = DateTime.UtcNow;
        await SaveAndReleaseOwnedLedgerAsync(ledger, cancellationToken);
        return ToOutcome(ledger, MongoMigrationStates.RolledBack);
    }

    private async Task<MongoMigrationOutcome> ApplyIndexesAsync(
        string migrationId,
        IReadOnlyList<MongoIndexSpecification> indexes,
        CancellationToken cancellationToken)
    {
        var checksum = Checksum(string.Join('|', indexes.Select(SerializeIndex)));
        var existing = await LoadLedgerAsync(migrationId, cancellationToken);
        if (existing is not null)
        {
            EnsureChecksum(existing, checksum);
            if (existing.State == MongoMigrationStates.Completed)
            {
                return ToOutcome(existing, MongoMigrationStates.Skipped);
            }
        }

        if (_options.DryRun)
        {
            return new MongoMigrationOutcome(migrationId, MongoMigrationStates.DryRun, indexes.Count);
        }

        var ledger = await GetOrCreateLedgerAsync(migrationId, checksum, cancellationToken);
        ledger = await AcquireLeaseAsync(ledger, cancellationToken);
        if (ledger.LeaseOwner != _owner)
        {
            return ToOutcome(ledger, MongoMigrationStates.Busy);
        }

        foreach (var group in indexes.GroupBy(x => (x.Module, x.Collection)))
        {
            var collection = mongo.GetCollection<BsonDocument>(group.Key.Collection, group.Key.Module);
            var models = group.Select(specification => new CreateIndexModel<BsonDocument>(
                specification.Keys,
                new CreateIndexOptions<BsonDocument>
                {
                    Name = specification.Name,
                    Unique = specification.Unique,
                    Collation = specification.CaseInsensitive
                        ? new Collation("en", strength: CollationStrength.Secondary)
                        : null,
                    ExpireAfter = specification.ExpireAfter,
                    PartialFilterExpression = specification.PartialFilter
                })).ToList();
            await collection.Indexes.CreateManyAsync(models, cancellationToken);
        }

        ledger.Examined = indexes.Count;
        ledger.Changed = indexes.Count;
        ledger.State = MongoMigrationStates.Completed;
        ledger.CompletedAt = DateTime.UtcNow;
        await SaveAndReleaseOwnedLedgerAsync(ledger, cancellationToken);
        return ToOutcome(ledger, MongoMigrationStates.Completed);
    }

    private async Task<MongoMigrationOutcome> ReplaceIdentityCredentialScalarUtcIndexesAsync(
        CancellationToken cancellationToken)
    {
        var indexes = MongoIdentityCredentialIndexes.All
            .Where(specification => specification.Name is
                "ix_refreshsessions_owner_active" or "ix_apikeys_owner_revoked_expires")
            .ToList();
        var checksum = Checksum(string.Join('|', indexes.Select(SerializeIndex)));
        var existing = await LoadLedgerAsync(
            IdentityCredentialScalarUtcIndexMigrationId,
            cancellationToken);
        if (existing is not null)
        {
            EnsureChecksum(existing, checksum);
            if (existing.State == MongoMigrationStates.Completed)
            {
                return ToOutcome(existing, MongoMigrationStates.Skipped);
            }
        }

        if (_options.DryRun)
        {
            return new MongoMigrationOutcome(
                IdentityCredentialScalarUtcIndexMigrationId,
                MongoMigrationStates.DryRun,
                indexes.Count);
        }

        var ledger = await GetOrCreateLedgerAsync(
            IdentityCredentialScalarUtcIndexMigrationId,
            checksum,
            cancellationToken);
        ledger = await AcquireLeaseAsync(ledger, cancellationToken);
        if (ledger.LeaseOwner != _owner)
        {
            return ToOutcome(ledger, MongoMigrationStates.Busy);
        }

        foreach (var specification in indexes)
        {
            var collection = mongo.GetCollection<BsonDocument>(
                specification.Collection,
                specification.Module);
            using var cursor = await collection.Indexes.ListAsync(cancellationToken);
            var current = (await cursor.ToListAsync(cancellationToken))
                .FirstOrDefault(index => index["name"].AsString == specification.Name);
            ledger.Examined++;
            if (current is not null && current["key"].AsBsonDocument == specification.Keys)
            {
                ledger.Skipped++;
                continue;
            }

            if (current is not null)
            {
                await collection.Indexes.DropOneAsync(specification.Name, cancellationToken);
            }

            await collection.Indexes.CreateOneAsync(
                new CreateIndexModel<BsonDocument>(
                    specification.Keys,
                    new CreateIndexOptions { Name = specification.Name }),
                cancellationToken: cancellationToken);
            ledger.Changed++;
            await SaveOwnedLedgerAsync(ledger, cancellationToken);
        }

        ledger.State = MongoMigrationStates.Completed;
        ledger.CompletedAt = DateTime.UtcNow;
        await SaveAndReleaseOwnedLedgerAsync(ledger, cancellationToken);
        return ToOutcome(ledger, MongoMigrationStates.Completed);
    }

    private async Task<MongoMigrationOutcome> ReplaceNotificationDeliveryIndexesAsync(
        CancellationToken cancellationToken)
    {
        var indexes = MongoNotificationDeliveryIndexes.All;
        var checksum = Checksum(string.Join('|', indexes.Select(SerializeIndex)));
        var existing = await LoadLedgerAsync(NotificationDeliveryIndexMigrationId, cancellationToken);
        if (existing is not null)
        {
            EnsureChecksum(existing, checksum);
            if (existing.State == MongoMigrationStates.Completed)
                return ToOutcome(existing, MongoMigrationStates.Skipped);
        }
        if (_options.DryRun)
            return new MongoMigrationOutcome(
                NotificationDeliveryIndexMigrationId,
                MongoMigrationStates.DryRun,
                indexes.Count);

        var ledger = await GetOrCreateLedgerAsync(
            NotificationDeliveryIndexMigrationId,
            checksum,
            cancellationToken);
        ledger = await AcquireLeaseAsync(ledger, cancellationToken);
        if (ledger.LeaseOwner != _owner)
            return ToOutcome(ledger, MongoMigrationStates.Busy);

        foreach (var specification in indexes)
        {
            var collection = mongo.GetCollection<BsonDocument>(
                specification.Collection,
                specification.Module);
            using var cursor = await collection.Indexes.ListAsync(cancellationToken);
            var current = (await cursor.ToListAsync(cancellationToken))
                .FirstOrDefault(index => index["name"].AsString == specification.Name);
            ledger.Examined++;
            if (current is not null)
                await collection.Indexes.DropOneAsync(specification.Name, cancellationToken);
            await collection.Indexes.CreateOneAsync(
                new CreateIndexModel<BsonDocument>(
                    specification.Keys,
                    new CreateIndexOptions<BsonDocument>
                    {
                        Name = specification.Name,
                        Unique = specification.Unique,
                        PartialFilterExpression = specification.PartialFilter
                    }),
                cancellationToken: cancellationToken);
            ledger.Changed++;
            await SaveOwnedLedgerAsync(ledger, cancellationToken);
        }

        ledger.State = MongoMigrationStates.Completed;
        ledger.CompletedAt = DateTime.UtcNow;
        await SaveAndReleaseOwnedLedgerAsync(ledger, cancellationToken);
        return ToOutcome(ledger, MongoMigrationStates.Completed);
    }

    private async Task<MongoMigrationOutcome> BackfillRanksAsync(CancellationToken cancellationToken)
    {
        var existing = await LoadLedgerAsync(RankMigrationId, cancellationToken);
        if (existing is not null)
        {
            EnsureChecksum(existing, RankChecksum);
            if (existing.State == MongoMigrationStates.Completed)
            {
                return ToOutcome(existing, MongoMigrationStates.Skipped);
            }
        }

        var workItems = mongo.GetCollection<BsonDocument>("workitems", WorkItemsModule);
        if (_options.DryRun)
        {
            var count = await workItems.CountDocumentsAsync(RankCandidateFilter(BsonNull.Value), cancellationToken: cancellationToken);
            return new MongoMigrationOutcome(RankMigrationId, MongoMigrationStates.DryRun, count);
        }

        var ledger = await GetOrCreateLedgerAsync(RankMigrationId, RankChecksum, cancellationToken);
        if (ledger.State == MongoMigrationStates.RolledBack)
        {
            ledger.State = MongoMigrationStates.Running;
            ledger.Examined = 0;
            ledger.Changed = 0;
            ledger.Skipped = 0;
            ledger.CompletedAt = null;
            ledger.RolledBackAt = null;
            await SaveLedgerAsync(ledger, cancellationToken);
        }

        ledger = await AcquireLeaseAsync(ledger, cancellationToken);
        if (ledger.LeaseOwner != _owner)
        {
            return ToOutcome(ledger, MongoMigrationStates.Busy);
        }

        var backups = mongo.GetCollection<MongoRankMigrationBackupDocument>(BackupCollection, WorkItemsModule);
        for (var batchNumber = 0; batchNumber < MaxBatches; batchNumber++)
        {
            var batch = await workItems.Find(RankCandidateFilter(ledger.Checkpoint))
                .Sort(new BsonDocument("_id", 1))
                .Limit(BatchSize)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                ledger.State = MongoMigrationStates.Completed;
                ledger.CompletedAt = DateTime.UtcNow;
                await SaveAndReleaseOwnedLedgerAsync(ledger, cancellationToken);
                return ToOutcome(ledger, MongoMigrationStates.Completed);
            }

            foreach (var document in batch)
            {
                ledger.Examined++;
                var id = document["_id"];
                if (!TryResolveRank(document.GetValue("CreatedAt", BsonNull.Value), out var rank))
                {
                    ledger.Skipped++;
                    continue;
                }

                var hadRank = document.TryGetValue("Rank", out var previousRank);
                var backup = new MongoRankMigrationBackupDocument
                {
                    Id = BackupId(id),
                    MigrationId = RankMigrationId,
                    DocumentId = id,
                    HadRank = hadRank,
                    PreviousRank = hadRank ? previousRank! : BsonNull.Value,
                    AppliedRank = rank
                };
                await backups.ReplaceOneAsync(
                    x => x.Id == backup.Id,
                    backup,
                    new ReplaceOptions { IsUpsert = true },
                    cancellationToken);

                var result = await workItems.UpdateOneAsync(
                    RankCandidateForId(id),
                    new BsonDocument("$set", new BsonDocument("Rank", rank)),
                    cancellationToken: cancellationToken);
                if (result.ModifiedCount == 1) ledger.Changed++; else ledger.Skipped++;
            }

            ledger.Checkpoint = batch[^1]["_id"];
            ledger.State = MongoMigrationStates.Paused;
            await SaveOwnedLedgerAsync(ledger, cancellationToken);
        }

        await SaveAndReleaseOwnedLedgerAsync(ledger, cancellationToken);
        return ToOutcome(ledger, MongoMigrationStates.Paused);
    }

    private async Task<MongoMigrationOutcome> BackfillRefreshSessionsAsync(CancellationToken cancellationToken)
    {
        var existing = await LoadLedgerAsync(RefreshSessionMigrationId, cancellationToken);
        if (existing is not null)
        {
            EnsureChecksum(existing, RefreshSessionChecksum);
            if (existing.State == MongoMigrationStates.Completed)
            {
                return ToOutcome(existing, MongoMigrationStates.Skipped);
            }
        }

        var users = mongo.GetCollection<BsonDocument>("users", "Identity");
        if (_options.DryRun)
        {
            var count = await users.CountDocumentsAsync(
                RefreshSessionUserFilter(BsonNull.Value),
                cancellationToken: cancellationToken);
            return new MongoMigrationOutcome(RefreshSessionMigrationId, MongoMigrationStates.DryRun, count);
        }

        var sessions = mongo.GetCollection<BsonDocument>("refreshsessions", "Identity");
        var ledger = await GetOrCreateLedgerAsync(
            RefreshSessionMigrationId,
            RefreshSessionChecksum,
            cancellationToken);
        ledger = await AcquireLeaseAsync(ledger, cancellationToken);
        if (ledger.LeaseOwner != _owner)
        {
            return ToOutcome(ledger, MongoMigrationStates.Busy);
        }

        for (var batchNumber = 0; batchNumber < MaxBatches; batchNumber++)
        {
            var batch = await users.Find(RefreshSessionUserFilter(ledger.Checkpoint))
                .Sort(new BsonDocument("_id", 1))
                .Limit(BatchSize)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                ledger.State = MongoMigrationStates.Completed;
                ledger.CompletedAt = DateTime.UtcNow;
                await SaveAndReleaseOwnedLedgerAsync(ledger, cancellationToken);
                return ToOutcome(ledger, MongoMigrationStates.Completed);
            }

            foreach (var user in batch)
            {
                ledger.Examined++;
                var userId = user["_id"].ToString() ?? string.Empty;
                var organizationId = StringValue(user, "OrganizationId") ?? string.Empty;
                foreach (var value in user["RefreshTokens"].AsBsonArray)
                {
                    if (!TryCreateRefreshSession(value, userId, organizationId, out var session))
                    {
                        ledger.Skipped++;
                        continue;
                    }

                    try
                    {
                        var result = await sessions.UpdateOneAsync(
                            Builders<BsonDocument>.Filter.Eq("_id", session["_id"]),
                            new BsonDocument("$setOnInsert", session),
                            new UpdateOptions { IsUpsert = true },
                            cancellationToken);
                        if (result.UpsertedId is null)
                        {
                            await EnsureRefreshSessionMatchesAsync(sessions, session, cancellationToken);
                            ledger.Skipped++;
                        }
                        else
                        {
                            ledger.Changed++;
                        }
                    }
                    catch (MongoWriteException exception)
                        when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
                    {
                        await EnsureRefreshSessionMatchesAsync(sessions, session, cancellationToken);
                        ledger.Skipped++;
                    }
                }

                await SaveOwnedLedgerAsync(ledger, cancellationToken);
            }

            ledger.Checkpoint = batch[^1]["_id"];
            ledger.State = MongoMigrationStates.Paused;
            await SaveOwnedLedgerAsync(ledger, cancellationToken);
        }

        await SaveAndReleaseOwnedLedgerAsync(ledger, cancellationToken);
        return ToOutcome(ledger, MongoMigrationStates.Paused);
    }

    private async Task<MongoMigrationOutcome> BackfillApiKeyVersionsAsync(CancellationToken cancellationToken)
    {
        var existing = await LoadLedgerAsync(ApiKeyVersionMigrationId, cancellationToken);
        if (existing is not null)
        {
            EnsureChecksum(existing, ApiKeyVersionChecksum);
            if (existing.State == MongoMigrationStates.Completed)
            {
                return ToOutcome(existing, MongoMigrationStates.Skipped);
            }
        }

        var apiKeys = mongo.GetCollection<BsonDocument>("apikeys", "Identity");
        if (_options.DryRun)
        {
            var count = await apiKeys.CountDocumentsAsync(
                ApiKeyVersionFilter(BsonNull.Value),
                cancellationToken: cancellationToken);
            return new MongoMigrationOutcome(ApiKeyVersionMigrationId, MongoMigrationStates.DryRun, count);
        }

        var ledger = await GetOrCreateLedgerAsync(
            ApiKeyVersionMigrationId,
            ApiKeyVersionChecksum,
            cancellationToken);
        ledger = await AcquireLeaseAsync(ledger, cancellationToken);
        if (ledger.LeaseOwner != _owner)
        {
            return ToOutcome(ledger, MongoMigrationStates.Busy);
        }

        for (var batchNumber = 0; batchNumber < MaxBatches; batchNumber++)
        {
            var batch = await apiKeys.Find(ApiKeyVersionFilter(ledger.Checkpoint))
                .Sort(new BsonDocument("_id", 1))
                .Limit(BatchSize)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                ledger.State = MongoMigrationStates.Completed;
                ledger.CompletedAt = DateTime.UtcNow;
                await SaveAndReleaseOwnedLedgerAsync(ledger, cancellationToken);
                return ToOutcome(ledger, MongoMigrationStates.Completed);
            }

            foreach (var document in batch)
            {
                ledger.Examined++;
                var updates = new List<UpdateDefinition<BsonDocument>>();
                var version = document.GetValue("Version", BsonNull.Value);
                if (version.IsBsonNull || (version.IsNumeric && version.ToInt64() <= 0))
                {
                    updates.Add(Builders<BsonDocument>.Update.Set("Version", 1L));
                }

                if (!document.Contains("ExpiresAtUtc")
                    && TryResolveUtc(document.GetValue("ExpiresAt", BsonNull.Value), out var expiresAtUtc))
                {
                    updates.Add(Builders<BsonDocument>.Update.Set("ExpiresAtUtc", expiresAtUtc));
                }

                if (!document.Contains("RevokedAtUtc")
                    && TryResolveUtc(document.GetValue("RevokedAt", BsonNull.Value), out var revokedAtUtc))
                {
                    updates.Add(Builders<BsonDocument>.Update.Set("RevokedAtUtc", revokedAtUtc));
                }

                if (updates.Count == 0)
                {
                    ledger.Skipped++;
                    continue;
                }

                var result = await apiKeys.UpdateOneAsync(
                    ApiKeyVersionForId(document["_id"]),
                    Builders<BsonDocument>.Update.Combine(updates),
                    cancellationToken: cancellationToken);
                if (result.ModifiedCount == 1) ledger.Changed++; else ledger.Skipped++;
                if (ledger.Examined % 100 == 0)
                {
                    await SaveOwnedLedgerAsync(ledger, cancellationToken);
                }
            }

            ledger.Checkpoint = batch[^1]["_id"];
            ledger.State = MongoMigrationStates.Paused;
            await SaveOwnedLedgerAsync(ledger, cancellationToken);
        }

        await SaveAndReleaseOwnedLedgerAsync(ledger, cancellationToken);
        return ToOutcome(ledger, MongoMigrationStates.Paused);
    }

    private async Task<MongoMigrationOutcome> BackfillOrganizationVersionsAsync(
        CancellationToken cancellationToken)
    {
        var existing = await LoadLedgerAsync(OrganizationVersionMigrationId, cancellationToken);
        if (existing is not null)
        {
            EnsureChecksum(existing, OrganizationVersionChecksum);
            if (existing.State == MongoMigrationStates.Completed)
            {
                return ToOutcome(existing, MongoMigrationStates.Skipped);
            }
        }

        var organizations = mongo.GetCollection<BsonDocument>("organizations", "Organizations");
        if (_options.DryRun)
        {
            var count = await organizations.CountDocumentsAsync(
                OrganizationVersionFilter(BsonNull.Value),
                cancellationToken: cancellationToken);
            return new MongoMigrationOutcome(
                OrganizationVersionMigrationId,
                MongoMigrationStates.DryRun,
                count);
        }

        var ledger = await GetOrCreateLedgerAsync(
            OrganizationVersionMigrationId,
            OrganizationVersionChecksum,
            cancellationToken);
        ledger = await AcquireLeaseAsync(ledger, cancellationToken);
        if (ledger.LeaseOwner != _owner)
        {
            return ToOutcome(ledger, MongoMigrationStates.Busy);
        }

        for (var batchNumber = 0; batchNumber < MaxBatches; batchNumber++)
        {
            var batch = await organizations.Find(OrganizationVersionFilter(ledger.Checkpoint))
                .Sort(new BsonDocument("_id", 1))
                .Limit(BatchSize)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                ledger.State = MongoMigrationStates.Completed;
                ledger.CompletedAt = DateTime.UtcNow;
                await SaveAndReleaseOwnedLedgerAsync(ledger, cancellationToken);
                return ToOutcome(ledger, MongoMigrationStates.Completed);
            }

            foreach (var document in batch)
            {
                ledger.Examined++;
                var updates = new List<UpdateDefinition<BsonDocument>>
                {
                    Builders<BsonDocument>.Update.Set("Version", 1L)
                };
                if (!document.Contains("Status") || document["Status"].IsBsonNull)
                {
                    updates.Add(Builders<BsonDocument>.Update.Set("Status", "Active"));
                }

                var result = await organizations.UpdateOneAsync(
                    OrganizationVersionForId(document["_id"]),
                    Builders<BsonDocument>.Update.Combine(updates),
                    cancellationToken: cancellationToken);
                if (result.ModifiedCount == 1) ledger.Changed++; else ledger.Skipped++;
            }

            ledger.Checkpoint = batch[^1]["_id"];
            ledger.State = MongoMigrationStates.Paused;
            await SaveOwnedLedgerAsync(ledger, cancellationToken);
        }

        await SaveAndReleaseOwnedLedgerAsync(ledger, cancellationToken);
        return ToOutcome(ledger, MongoMigrationStates.Paused);
    }

    private async Task<MongoMigrationOutcome> ExpireLegacyTeamInvitesAsync(
        CancellationToken cancellationToken)
    {
        var existing = await LoadLedgerAsync(TeamInviteTokenMigrationId, cancellationToken);
        if (existing is not null)
        {
            EnsureChecksum(existing, TeamInviteTokenChecksum);
            if (existing.State == MongoMigrationStates.Completed)
            {
                return ToOutcome(existing, MongoMigrationStates.Skipped);
            }
        }

        var teams = mongo.GetCollection<BsonDocument>("teams", "Teams");
        if (_options.DryRun)
        {
            var count = await teams.CountDocumentsAsync(
                LegacyTeamInviteFilter(BsonNull.Value),
                cancellationToken: cancellationToken);
            return new MongoMigrationOutcome(
                TeamInviteTokenMigrationId,
                MongoMigrationStates.DryRun,
                count);
        }

        var ledger = await GetOrCreateLedgerAsync(
            TeamInviteTokenMigrationId,
            TeamInviteTokenChecksum,
            cancellationToken);
        ledger = await AcquireLeaseAsync(ledger, cancellationToken);
        if (ledger.LeaseOwner != _owner)
        {
            return ToOutcome(ledger, MongoMigrationStates.Busy);
        }

        for (var batchNumber = 0; batchNumber < MaxBatches; batchNumber++)
        {
            var batch = await teams.Find(LegacyTeamInviteFilter(ledger.Checkpoint))
                .Sort(new BsonDocument("_id", 1))
                .Limit(BatchSize)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                ledger.State = MongoMigrationStates.Completed;
                ledger.CompletedAt = DateTime.UtcNow;
                await SaveAndReleaseOwnedLedgerAsync(ledger, cancellationToken);
                return ToOutcome(ledger, MongoMigrationStates.Completed);
            }

            foreach (var document in batch)
            {
                ledger.Examined++;
                var version = Math.Max(NumericTicks(document.GetValue("Version", 0)), 0);
                var changed = false;
                foreach (var memberValue in document.GetValue("Members", new BsonArray()).AsBsonArray)
                {
                    if (!memberValue.IsBsonDocument)
                    {
                        continue;
                    }

                    var member = memberValue.AsBsonDocument;
                    var tokenHash = member.GetValue("InvitationTokenHash", BsonNull.Value);
                    if (member.GetValue("Status", string.Empty) != "Invited"
                        || (tokenHash.IsString && !string.IsNullOrWhiteSpace(tokenHash.AsString)))
                    {
                        continue;
                    }

                    member["Status"] = "Expired";
                    member["InvitationTokenHash"] = BsonNull.Value;
                    member["InvitationExpiresAt"] = BsonNull.Value;
                    member["RespondedAt"] = DateTime.UtcNow;
                    changed = true;
                }

                if (changed)
                {
                    document["Version"] = version + 1;
                    document["TeamInviteTokenMigratedBy"] = TeamInviteTokenMigrationId;
                    var result = await teams.ReplaceOneAsync(
                        TeamVersionForId(document["_id"], version),
                        document,
                        cancellationToken: cancellationToken);
                    if (result.ModifiedCount == 1) ledger.Changed++; else ledger.Skipped++;
                }
                else
                {
                    ledger.Skipped++;
                }
            }

            ledger.Checkpoint = batch[^1]["_id"];
            ledger.State = MongoMigrationStates.Paused;
            await SaveOwnedLedgerAsync(ledger, cancellationToken);
        }

        await SaveAndReleaseOwnedLedgerAsync(ledger, cancellationToken);
        return ToOutcome(ledger, MongoMigrationStates.Paused);
    }

    private async Task<MongoMigrationOutcome> BackfillProjectLifecycleAsync(
        CancellationToken cancellationToken)
    {
        var existing = await LoadLedgerAsync(ProjectLifecycleMigrationId, cancellationToken);
        if (existing is not null)
        {
            EnsureChecksum(existing, ProjectLifecycleChecksum);
            if (existing.State == MongoMigrationStates.Completed)
            {
                return ToOutcome(existing, MongoMigrationStates.Skipped);
            }
        }

        var projects = mongo.GetCollection<BsonDocument>("projects", "Projects");
        if (_options.DryRun)
        {
            var count = await projects.CountDocumentsAsync(
                ProjectLifecycleFilter(BsonNull.Value),
                cancellationToken: cancellationToken);
            return new MongoMigrationOutcome(ProjectLifecycleMigrationId, MongoMigrationStates.DryRun, count);
        }

        var ledger = await GetOrCreateLedgerAsync(
            ProjectLifecycleMigrationId,
            ProjectLifecycleChecksum,
            cancellationToken);
        ledger = await AcquireLeaseAsync(ledger, cancellationToken);
        if (ledger.LeaseOwner != _owner)
        {
            return ToOutcome(ledger, MongoMigrationStates.Busy);
        }

        for (var batchNumber = 0; batchNumber < MaxBatches; batchNumber++)
        {
            var batch = await projects.Find(ProjectLifecycleFilter(ledger.Checkpoint))
                .Sort(new BsonDocument("_id", 1))
                .Limit(BatchSize)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                ledger.State = MongoMigrationStates.Completed;
                ledger.CompletedAt = DateTime.UtcNow;
                await SaveAndReleaseOwnedLedgerAsync(ledger, cancellationToken);
                return ToOutcome(ledger, MongoMigrationStates.Completed);
            }

            foreach (var document in batch)
            {
                ledger.Examined++;
                var version = Math.Max(NumericTicks(document.GetValue("Version", 0)), 0);
                var updates = new List<UpdateDefinition<BsonDocument>>
                {
                    Builders<BsonDocument>.Update.Set("Version", version + 1),
                    Builders<BsonDocument>.Update.Set("ProjectLifecycleMigratedBy", ProjectLifecycleMigrationId)
                };
                AddProjectDefault(document, updates, "Visibility", "Internal");
                AddProjectDefault(document, updates, "Archived", false);
                AddProjectDefault(document, updates, "Members", new BsonArray());
                AddProjectDefault(document, updates, "TeamIds", new BsonArray());
                AddProjectDefault(document, updates, "Templates", new BsonArray());
                AddProjectDefault(document, updates, "Components", new BsonArray());
                AddProjectDefault(document, updates, "Versions", new BsonArray());
                AddProjectDefault(document, updates, "Releases", new BsonArray());
                AddProjectDefault(document, updates, "Milestones", new BsonArray());
                AddProjectDefault(document, updates, "ArchivedAt", BsonNull.Value);
                AddProjectDefault(document, updates, "RetainUntil", BsonNull.Value);

                var result = await projects.UpdateOneAsync(
                    ProjectVersionForId(document["_id"], version),
                    Builders<BsonDocument>.Update.Combine(updates),
                    cancellationToken: cancellationToken);
                if (result.ModifiedCount == 1) ledger.Changed++; else ledger.Skipped++;
            }

            ledger.Checkpoint = batch[^1]["_id"];
            ledger.State = MongoMigrationStates.Paused;
            await SaveOwnedLedgerAsync(ledger, cancellationToken);
        }

        await SaveAndReleaseOwnedLedgerAsync(ledger, cancellationToken);
        return ToOutcome(ledger, MongoMigrationStates.Paused);
    }

    private static void AddProjectDefault(
        BsonDocument document,
        ICollection<UpdateDefinition<BsonDocument>> updates,
        string field,
        BsonValue value)
    {
        if (!document.Contains(field) || document[field].IsBsonNull)
        {
            updates.Add(Builders<BsonDocument>.Update.Set(field, value));
        }
    }

    private async Task<MongoMigrationOutcome> BackfillWorkflowLifecycleAsync(
        CancellationToken cancellationToken)
    {
        var existing = await LoadLedgerAsync(WorkflowLifecycleMigrationId, cancellationToken);
        if (existing is not null)
        {
            EnsureChecksum(existing, WorkflowLifecycleChecksum);
            if (existing.State == MongoMigrationStates.Completed)
            {
                return ToOutcome(existing, MongoMigrationStates.Skipped);
            }
        }

        var workflows = mongo.GetCollection<BsonDocument>("workflows", "Workflows");
        if (_options.DryRun)
        {
            var count = await workflows.CountDocumentsAsync(
                WorkflowLifecycleFilter(BsonNull.Value),
                cancellationToken: cancellationToken);
            return new MongoMigrationOutcome(WorkflowLifecycleMigrationId, MongoMigrationStates.DryRun, count);
        }

        var ledger = await GetOrCreateLedgerAsync(
            WorkflowLifecycleMigrationId,
            WorkflowLifecycleChecksum,
            cancellationToken);
        ledger = await AcquireLeaseAsync(ledger, cancellationToken);
        if (ledger.LeaseOwner != _owner)
        {
            return ToOutcome(ledger, MongoMigrationStates.Busy);
        }

        for (var batchNumber = 0; batchNumber < MaxBatches; batchNumber++)
        {
            var batch = await workflows.Find(WorkflowLifecycleFilter(ledger.Checkpoint))
                .Sort(new BsonDocument("_id", 1))
                .Limit(BatchSize)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                ledger.State = MongoMigrationStates.Completed;
                ledger.CompletedAt = DateTime.UtcNow;
                await SaveAndReleaseOwnedLedgerAsync(ledger, cancellationToken);
                return ToOutcome(ledger, MongoMigrationStates.Completed);
            }

            foreach (var document in batch)
            {
                ledger.Examined++;
                var version = Math.Max(NumericTicks(document.GetValue("Version", 0)), 0);
                var statuses = document.GetValue("Statuses", new BsonArray()).AsBsonArray;
                var transitions = document.GetValue("Transitions", new BsonArray()).AsBsonArray;
                var defaultStatus = statuses
                    .Where(x => x.IsBsonDocument && x.AsBsonDocument.GetValue("Category", "") == "Todo")
                    .Select(x => x.AsBsonDocument.GetValue("Name", "To Do").AsString)
                    .FirstOrDefault() ?? "To Do";
                var done = new BsonArray(statuses
                    .Where(x => x.IsBsonDocument && x.AsBsonDocument.GetValue("Category", "") == "Done")
                    .Select(x => x.AsBsonDocument.GetValue("Name", "Done")));
                var names = new BsonArray(statuses
                    .Where(x => x.IsBsonDocument)
                    .Select(x => x.AsBsonDocument.GetValue("Name", "")));
                var schemes = new BsonArray
                {
                    new BsonDocument
                    {
                        ["IssueType"] = "*",
                        ["DefaultStatus"] = defaultStatus,
                        ["Statuses"] = names,
                        ["DoneStatuses"] = done
                    }
                };
                var createdAt = document.GetValue("CreatedAt", DateTime.UtcNow);
                var published = new BsonDocument
                {
                    ["Number"] = 1,
                    ["State"] = "Published",
                    ["Statuses"] = statuses,
                    ["Transitions"] = transitions,
                    ["IssueTypeSchemes"] = schemes,
                    ["CreatedAt"] = createdAt,
                    ["PublishedAt"] = document.GetValue("UpdatedAt", createdAt)
                };
                var update = Builders<BsonDocument>.Update
                    .Set("Version", version + 1)
                    .Set("PublishedVersion", 1)
                    .Set("IssueTypeSchemes", schemes)
                    .Set("Draft", BsonNull.Value)
                    .Set("PublishedVersions", new BsonArray { published })
                    .Set("WorkflowLifecycleMigratedBy", WorkflowLifecycleMigrationId);
                var result = await workflows.UpdateOneAsync(
                    WorkflowVersionForId(document["_id"], version),
                    update,
                    cancellationToken: cancellationToken);
                if (result.ModifiedCount == 1) ledger.Changed++; else ledger.Skipped++;
            }

            ledger.Checkpoint = batch[^1]["_id"];
            ledger.State = MongoMigrationStates.Paused;
            await SaveOwnedLedgerAsync(ledger, cancellationToken);
        }

        await SaveAndReleaseOwnedLedgerAsync(ledger, cancellationToken);
        return ToOutcome(ledger, MongoMigrationStates.Paused);
    }

    private async Task<MongoMigrationOutcome> BackfillSprintLifecycleAsync(
        CancellationToken cancellationToken)
    {
        var existing = await LoadLedgerAsync(SprintLifecycleMigrationId, cancellationToken);
        if (existing is not null)
        {
            EnsureChecksum(existing, SprintLifecycleChecksum);
            if (existing.State == MongoMigrationStates.Completed)
            {
                return ToOutcome(existing, MongoMigrationStates.Skipped);
            }
        }

        var workItems = mongo.GetCollection<BsonDocument>("workitems", WorkItemsModule);
        if (_options.DryRun)
        {
            var count = await workItems.CountDocumentsAsync(
                SprintLifecycleFilter(BsonNull.Value),
                cancellationToken: cancellationToken);
            return new MongoMigrationOutcome(SprintLifecycleMigrationId, MongoMigrationStates.DryRun, count);
        }

        var ledger = await GetOrCreateLedgerAsync(
            SprintLifecycleMigrationId,
            SprintLifecycleChecksum,
            cancellationToken);
        ledger = await AcquireLeaseAsync(ledger, cancellationToken);
        if (ledger.LeaseOwner != _owner)
        {
            return ToOutcome(ledger, MongoMigrationStates.Busy);
        }

        var sprints = mongo.GetCollection<BsonDocument>("sprints", WorkItemsModule);
        for (var batchNumber = 0; batchNumber < MaxBatches; batchNumber++)
        {
            var batch = await workItems.Find(SprintLifecycleFilter(ledger.Checkpoint))
                .Sort(new BsonDocument("_id", 1))
                .Limit(BatchSize)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                ledger.State = MongoMigrationStates.Completed;
                ledger.CompletedAt = DateTime.UtcNow;
                await SaveAndReleaseOwnedLedgerAsync(ledger, cancellationToken);
                return ToOutcome(ledger, MongoMigrationStates.Completed);
            }

            foreach (var document in batch)
            {
                ledger.Examined++;
                var projectId = StringValue(document, "ProjectId") ?? string.Empty;
                var legacySprintId = StringValue(document, "SprintId") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(legacySprintId))
                {
                    ledger.Skipped++;
                    continue;
                }

                var sprintId = LegacySprintId(projectId, legacySprintId);
                var now = DateTime.UtcNow;
                await sprints.UpdateOneAsync(
                    Builders<BsonDocument>.Filter.Eq("_id", sprintId),
                    new BsonDocument("$setOnInsert", new BsonDocument
                    {
                        ["_id"] = sprintId,
                        ["ProjectId"] = projectId,
                        ["Name"] = $"{legacySprintId} (legacy-{sprintId[^8..]})",
                        ["Goal"] = "Legacy sprint backfill",
                        ["StartAtUtc"] = now,
                        ["EndAtUtc"] = now.AddDays(13),
                        ["Status"] = "Planned",
                        ["CommittedItems"] = 0,
                        ["CommittedPoints"] = 0,
                        ["CompletedItems"] = 0,
                        ["CompletedPoints"] = 0,
                        ["CarryoverItems"] = 0,
                        ["CarryoverPoints"] = 0,
                        ["CreatedAt"] = now,
                        ["UpdatedAt"] = now,
                        ["Version"] = 0
                    }),
                    new UpdateOptions { IsUpsert = true },
                    cancellationToken);

                var version = Math.Max(NumericTicks(document.GetValue("Version", 0)), 0);
                var update = Builders<BsonDocument>.Update
                    .Set("SprintId", sprintId)
                    .Set("SprintLifecycleMigratedBy", SprintLifecycleMigrationId)
                    .Set("Version", version + 1);
                var result = await workItems.UpdateOneAsync(
                    WorkflowVersionForId(document["_id"], version),
                    update,
                    cancellationToken: cancellationToken);
                if (result.ModifiedCount == 1) ledger.Changed++; else ledger.Skipped++;
            }

            ledger.Checkpoint = batch[^1]["_id"];
            ledger.State = MongoMigrationStates.Paused;
            await SaveOwnedLedgerAsync(ledger, cancellationToken);
        }

        await SaveAndReleaseOwnedLedgerAsync(ledger, cancellationToken);
        return ToOutcome(ledger, MongoMigrationStates.Paused);
    }

    private async Task<MongoMigrationOutcome> BackfillWorkItemTypeSchemasAsync(
        CancellationToken cancellationToken)
    {
        var existing = await LoadLedgerAsync(WorkItemTypeSchemaMigrationId, cancellationToken);
        if (existing is not null)
        {
            EnsureChecksum(existing, WorkItemTypeSchemaChecksum);
            if (existing.State == MongoMigrationStates.Completed)
            {
                return ToOutcome(existing, MongoMigrationStates.Skipped);
            }
        }

        var workItems = mongo.GetCollection<BsonDocument>("workitems", WorkItemsModule);
        if (_options.DryRun)
        {
            var count = await workItems.CountDocumentsAsync(
                WorkItemTypeSchemaFilter(BsonNull.Value),
                cancellationToken: cancellationToken);
            return new MongoMigrationOutcome(
                WorkItemTypeSchemaMigrationId,
                MongoMigrationStates.DryRun,
                count);
        }

        var ledger = await GetOrCreateLedgerAsync(
            WorkItemTypeSchemaMigrationId,
            WorkItemTypeSchemaChecksum,
            cancellationToken);
        ledger = await AcquireLeaseAsync(ledger, cancellationToken);
        if (ledger.LeaseOwner != _owner)
        {
            return ToOutcome(ledger, MongoMigrationStates.Busy);
        }

        var schemas = mongo.GetCollection<BsonDocument>("workitemtypeschemas", WorkItemsModule);
        for (var batchNumber = 0; batchNumber < MaxBatches; batchNumber++)
        {
            var batch = await workItems.Find(WorkItemTypeSchemaFilter(ledger.Checkpoint))
                .Sort(new BsonDocument("_id", 1))
                .Limit(BatchSize)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                ledger.State = MongoMigrationStates.Completed;
                ledger.CompletedAt = DateTime.UtcNow;
                await SaveAndReleaseOwnedLedgerAsync(ledger, cancellationToken);
                return ToOutcome(ledger, MongoMigrationStates.Completed);
            }

            foreach (var document in batch)
            {
                ledger.Examined++;
                var projectId = StringValue(document, "ProjectId") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(projectId))
                {
                    ledger.Skipped++;
                    continue;
                }

                var now = DateTime.UtcNow;
                await schemas.UpdateOneAsync(
                    Builders<BsonDocument>.Filter.Eq("_id", projectId),
                    new BsonDocument("$setOnInsert", new BsonDocument
                    {
                        ["_id"] = projectId,
                        ["ProjectId"] = projectId,
                        ["SchemaVersion"] = 1,
                        ["IssueTypes"] = DefaultIssueTypes(),
                        ["CustomFields"] = new BsonArray(),
                        ["Layouts"] = DefaultIssueTypeLayouts(),
                        ["CreatedAt"] = now,
                        ["UpdatedAt"] = now,
                        ["Version"] = 0
                    }),
                    new UpdateOptions { IsUpsert = true },
                    cancellationToken);

                var issueType = StringValue(document, "Type") ?? "Task";
                if (!DefaultIssueTypeKeys.Contains(issueType, StringComparer.OrdinalIgnoreCase))
                {
                    await schemas.UpdateOneAsync(
                        Builders<BsonDocument>.Filter.Eq("_id", projectId)
                        & Builders<BsonDocument>.Filter.Ne("IssueTypes.Key", issueType),
                        Builders<BsonDocument>.Update
                            .Push("IssueTypes", IssueType(issueType, issueType, "Standard", 100))
                            .Push("Layouts", new BsonDocument
                            {
                                ["IssueTypeKey"] = issueType,
                                ["FieldKeys"] = new BsonArray()
                            })
                            .Inc("SchemaVersion", 1)
                            .Inc("Version", 1)
                            .Set("UpdatedAt", now),
                        cancellationToken: cancellationToken);
                }

                var version = Math.Max(NumericTicks(document.GetValue("Version", 0)), 0);
                var result = await workItems.UpdateOneAsync(
                    WorkflowVersionForId(document["_id"], version),
                    Builders<BsonDocument>.Update
                        .Set("IssueTypeSchemaVersion", 1)
                        .Set("CustomFields", new BsonArray())
                        .Set("WorkItemTypeSchemaMigratedBy", WorkItemTypeSchemaMigrationId)
                        .Set("Version", version + 1),
                    cancellationToken: cancellationToken);
                if (result.ModifiedCount == 1) ledger.Changed++; else ledger.Skipped++;
            }

            ledger.Checkpoint = batch[^1]["_id"];
            ledger.State = MongoMigrationStates.Paused;
            await SaveOwnedLedgerAsync(ledger, cancellationToken);
        }

        await SaveAndReleaseOwnedLedgerAsync(ledger, cancellationToken);
        return ToOutcome(ledger, MongoMigrationStates.Paused);
    }

    private async Task<MongoMigrationOutcome> BackfillWorkItemGraphAsync(
        CancellationToken cancellationToken)
    {
        var existing = await LoadLedgerAsync(WorkItemGraphMigrationId, cancellationToken);
        if (existing is not null)
        {
            EnsureChecksum(existing, WorkItemGraphChecksum);
            if (existing.State == MongoMigrationStates.Completed)
            {
                return ToOutcome(existing, MongoMigrationStates.Skipped);
            }
        }

        var workItems = mongo.GetCollection<BsonDocument>("workitems", WorkItemsModule);
        if (_options.DryRun)
        {
            var count = await workItems.CountDocumentsAsync(
                WorkItemGraphFilter(BsonNull.Value),
                cancellationToken: cancellationToken);
            return new MongoMigrationOutcome(
                WorkItemGraphMigrationId,
                MongoMigrationStates.DryRun,
                count);
        }

        var ledger = await GetOrCreateLedgerAsync(
            WorkItemGraphMigrationId,
            WorkItemGraphChecksum,
            cancellationToken);
        ledger = await AcquireLeaseAsync(ledger, cancellationToken);
        if (ledger.LeaseOwner != _owner)
        {
            return ToOutcome(ledger, MongoMigrationStates.Busy);
        }

        var edges = mongo.GetCollection<BsonDocument>("workitemrelationedges", WorkItemsModule);
        for (var batchNumber = 0; batchNumber < MaxBatches; batchNumber++)
        {
            var batch = await workItems.Find(WorkItemGraphFilter(ledger.Checkpoint))
                .Sort(new BsonDocument("_id", 1))
                .Limit(BatchSize)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                ledger.State = MongoMigrationStates.Completed;
                ledger.CompletedAt = DateTime.UtcNow;
                await SaveAndReleaseOwnedLedgerAsync(ledger, cancellationToken);
                return ToOutcome(ledger, MongoMigrationStates.Completed);
            }

            foreach (var workItem in batch)
            {
                ledger.Examined++;
                var sourceId = StringValue(workItem, "_id");
                var projectId = StringValue(workItem, "ProjectId");
                if (string.IsNullOrWhiteSpace(sourceId) || string.IsNullOrWhiteSpace(projectId))
                {
                    ledger.Skipped++;
                    continue;
                }

                foreach (var value in ArrayValue(workItem, "Relations"))
                {
                    if (!value.IsBsonDocument)
                    {
                        ledger.Skipped++;
                        continue;
                    }

                    var relation = value.AsBsonDocument;
                    var targetId = StringValue(relation, "RelatedWorkItemId");
                    var relationType = NormalizeGraphRelationType(StringValue(relation, "RelationType"));
                    if (string.IsNullOrWhiteSpace(targetId) || relationType is null)
                    {
                        ledger.Skipped++;
                        continue;
                    }

                    var (dependencyFrom, dependencyTo) = relationType switch
                    {
                        "Blocks" => (sourceId, targetId),
                        "BlockedBy" => (targetId, sourceId),
                        _ => ((string?)null, (string?)null)
                    };
                    var id = WorkItemRelationEdgeId(projectId, sourceId, targetId, relationType);
                    var edge = new BsonDocument
                    {
                        ["_id"] = id,
                        ["ProjectId"] = projectId,
                        ["SourceWorkItemId"] = sourceId,
                        ["TargetWorkItemId"] = targetId,
                        ["RelationType"] = relationType,
                        ["DependencyFromWorkItemId"] = dependencyFrom is null ? BsonNull.Value : dependencyFrom,
                        ["DependencyToWorkItemId"] = dependencyTo is null ? BsonNull.Value : dependencyTo,
                        ["CreatedAt"] = DateTime.UtcNow,
                        ["Version"] = 0L
                    };
                    var result = await edges.UpdateOneAsync(
                        Builders<BsonDocument>.Filter.Eq("_id", id),
                        new BsonDocument("$setOnInsert", edge),
                        new UpdateOptions { IsUpsert = true },
                        cancellationToken);
                    if (result.UpsertedId is null) ledger.Skipped++; else ledger.Changed++;
                }
            }

            ledger.Checkpoint = batch[^1]["_id"];
            ledger.State = MongoMigrationStates.Paused;
            await SaveOwnedLedgerAsync(ledger, cancellationToken);
        }

        await SaveAndReleaseOwnedLedgerAsync(ledger, cancellationToken);
        return ToOutcome(ledger, MongoMigrationStates.Paused);
    }

    private async Task<MongoMigrationOutcome> BackfillWorkItemActivitiesAsync(CancellationToken cancellationToken)
    {
        var existing = await LoadLedgerAsync(WorkItemActivityMigrationId, cancellationToken);
        if (existing is not null)
        {
            EnsureChecksum(existing, WorkItemActivityChecksum);
            if (existing.State == MongoMigrationStates.Completed)
            {
                return ToOutcome(existing, MongoMigrationStates.Skipped);
            }
        }

        var workItems = mongo.GetCollection<BsonDocument>("workitems", WorkItemsModule);
        if (_options.DryRun)
        {
            var count = await workItems.CountDocumentsAsync(
                WorkItemActivityFilter(BsonNull.Value),
                cancellationToken: cancellationToken);
            return new MongoMigrationOutcome(WorkItemActivityMigrationId, MongoMigrationStates.DryRun, count);
        }

        var ledger = await GetOrCreateLedgerAsync(
            WorkItemActivityMigrationId,
            WorkItemActivityChecksum,
            cancellationToken);
        ledger = await AcquireLeaseAsync(ledger, cancellationToken);
        if (ledger.LeaseOwner != _owner)
        {
            return ToOutcome(ledger, MongoMigrationStates.Busy);
        }

        var projects = mongo.GetCollection<BsonDocument>("projects", "Projects");
        for (var batchNumber = 0; batchNumber < MaxBatches; batchNumber++)
        {
            var batch = await workItems.Find(WorkItemActivityFilter(ledger.Checkpoint))
                .Sort(new BsonDocument("_id", 1))
                .Limit(BatchSize)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                ledger.State = MongoMigrationStates.Completed;
                ledger.CompletedAt = DateTime.UtcNow;
                await SaveAndReleaseOwnedLedgerAsync(ledger, cancellationToken);
                return ToOutcome(ledger, MongoMigrationStates.Completed);
            }

            foreach (var workItem in batch)
            {
                ledger.Examined++;
                var workItemId = workItem["_id"].ToString() ?? string.Empty;
                var projectId = StringValue(workItem, "ProjectId") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(workItemId))
                {
                    throw new InvalidOperationException(
                        "A work item with an empty identifier cannot be migrated.");
                }

                if (!HasMigratableActivities(workItem))
                {
                    ledger.Skipped++;
                    await SaveOwnedLedgerAsync(ledger, cancellationToken);
                    continue;
                }

                var project = await projects.Find(Builders<BsonDocument>.Filter.Eq("_id", projectId))
                    .FirstOrDefaultAsync(cancellationToken);
                var organizationId = project is null ? null : StringValue(project, "OrganizationId");
                if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(organizationId))
                {
                    throw new InvalidOperationException(
                        $"Work item '{workItemId}' cannot be migrated because project tenant ownership is missing.");
                }

                await UpsertWorkItemActivitiesAsync(
                    workItem,
                    organizationId,
                    projectId,
                    workItemId,
                    cancellationToken);

                var currentVersion = workItem.GetValue("Version", 0L).ToInt64();
                var versionFilter = workItem.Contains("Version")
                    ? Builders<BsonDocument>.Filter.Eq("Version", currentVersion)
                    : Builders<BsonDocument>.Filter.Exists("Version", false);
                var update = Builders<BsonDocument>.Update
                    .Set("ActivityStorageVersion", 1)
                    .Set("Comments", new BsonArray())
                    .Set("Attachments", new BsonArray())
                    .Set("WorkLogs", new BsonArray())
                    .Set("Approvals", new BsonArray())
                    .Set("StatusHistory", new BsonArray())
                    .Set("Version", checked(currentVersion + 1));
                var result = await workItems.UpdateOneAsync(
                    Builders<BsonDocument>.Filter.Eq("_id", workItem["_id"])
                    & versionFilter
                    & WorkItemActivityVersionFilter(),
                    update,
                    cancellationToken: cancellationToken);
                if (result.ModifiedCount == 1) ledger.Changed++; else ledger.Skipped++;
                await SaveOwnedLedgerAsync(ledger, cancellationToken);
            }

            ledger.Checkpoint = batch[^1]["_id"];
            ledger.State = MongoMigrationStates.Paused;
            await SaveOwnedLedgerAsync(ledger, cancellationToken);
        }

        await SaveAndReleaseOwnedLedgerAsync(ledger, cancellationToken);
        return ToOutcome(ledger, MongoMigrationStates.Paused);
    }

    private async Task UpsertWorkItemActivitiesAsync(
        BsonDocument workItem,
        string organizationId,
        string projectId,
        string workItemId,
        CancellationToken cancellationToken)
    {
        var comments = mongo.GetCollection<BsonDocument>("workitemcommentactivitys", WorkItemsModule);
        var revisions = mongo.GetCollection<BsonDocument>("workitemcommentrevisionactivitys", WorkItemsModule);
        var attachments = mongo.GetCollection<BsonDocument>("workitemattachmentactivitys", WorkItemsModule);
        var workLogs = mongo.GetCollection<BsonDocument>("workitemworklogactivitys", WorkItemsModule);
        var approvals = mongo.GetCollection<BsonDocument>("workitemapprovalactivitys", WorkItemsModule);
        var timeline = mongo.GetCollection<BsonDocument>("workitemtimelineactivitys", WorkItemsModule);

        foreach (var value in ArrayValue(workItem, "Comments"))
        {
            if (!value.IsBsonDocument) continue;
            var source = value.AsBsonDocument;
            var commentId = StringValue(source, "Id") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(commentId)) continue;
            await ReplaceMigratedActivityAsync(comments, new BsonDocument
            {
                ["_id"] = commentId,
                ["OrganizationId"] = organizationId,
                ["ProjectId"] = projectId,
                ["WorkItemId"] = workItemId,
                ["Body"] = source.GetValue("Body", string.Empty),
                ["AuthorUserId"] = source.GetValue("AuthorUserId", "system"),
                ["Mentions"] = source.GetValue("Mentions", new BsonArray()),
                ["CreatedAt"] = source.GetValue("CreatedAt", BsonNull.Value),
                ["EditedAt"] = source.GetValue("EditedAt", BsonNull.Value),
                ["Version"] = 0L
            }, cancellationToken);

            var history = ArrayValue(source, "History");
            for (var ordinal = 0; ordinal < history.Count; ordinal++)
            {
                if (!history[ordinal].IsBsonDocument) continue;
                var revision = history[ordinal].AsBsonDocument;
                var editedAt = revision.GetValue("EditedAt", BsonNull.Value);
                if (!TryResolveUtc(editedAt, out var editedAtUtc)) continue;
                await ReplaceMigratedActivityAsync(revisions, new BsonDocument
                {
                    ["_id"] = ActivityId("revision", workItemId, commentId, ordinal.ToString(), editedAtUtc.Ticks.ToString()),
                    ["OrganizationId"] = organizationId,
                    ["ProjectId"] = projectId,
                    ["WorkItemId"] = workItemId,
                    ["CommentId"] = commentId,
                    ["Body"] = revision.GetValue("Body", string.Empty),
                    ["EditedByUserId"] = revision.GetValue("EditedByUserId", "system"),
                    ["EditedAt"] = editedAt,
                    ["Version"] = 0L
                }, cancellationToken);
            }
        }

        await CopyArrayAsync(attachments, workItem, "Attachments", organizationId, projectId, workItemId,
            ["FileName", "ContentType", "SizeBytes", "StoragePath", "ChecksumSha256", "CreatedAt"], cancellationToken);
        await CopyArrayAsync(workLogs, workItem, "WorkLogs", organizationId, projectId, workItemId,
            ["UserId", "Hours", "Note", "CreatedAt"], cancellationToken);
        await CopyArrayAsync(approvals, workItem, "Approvals", organizationId, projectId, workItemId,
            ["FromStatus", "ToStatus", "RequestedByUserId", "RequestedAt", "ExpiresAt", "Status",
                "DecidedByUserId", "DecidedAt", "Note", "ConsumedAt"], cancellationToken);

        var historyEntries = ArrayValue(workItem, "StatusHistory");
        for (var ordinal = 0; ordinal < historyEntries.Count; ordinal++)
        {
            if (!historyEntries[ordinal].IsBsonDocument) continue;
            var source = historyEntries[ordinal].AsBsonDocument;
            var changedAt = source.GetValue("ChangedAt", BsonNull.Value);
            var toStatus = StringValue(source, "ToStatus") ?? string.Empty;
            if (!TryResolveUtc(changedAt, out var changedAtUtc) || string.IsNullOrWhiteSpace(toStatus)) continue;
            await ReplaceMigratedActivityAsync(timeline, new BsonDocument
            {
                ["_id"] = ActivityId("timeline", workItemId, ordinal.ToString(), changedAtUtc.Ticks.ToString(), toStatus),
                ["OrganizationId"] = organizationId,
                ["ProjectId"] = projectId,
                ["WorkItemId"] = workItemId,
                ["FromStatus"] = source.GetValue("FromStatus", BsonNull.Value),
                ["ToStatus"] = toStatus,
                ["ChangedByUserId"] = source.GetValue("ChangedByUserId", "system"),
                ["ChangedAt"] = changedAt,
                ["Version"] = 0L
            }, cancellationToken);
        }
    }

    private async Task CopyArrayAsync(
        IMongoCollection<BsonDocument> target,
        BsonDocument workItem,
        string field,
        string organizationId,
        string projectId,
        string workItemId,
        IReadOnlyCollection<string> copiedFields,
        CancellationToken cancellationToken)
    {
        foreach (var value in ArrayValue(workItem, field))
        {
            if (!value.IsBsonDocument) continue;
            var source = value.AsBsonDocument;
            var id = StringValue(source, "Id") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(id)) continue;
            var activity = new BsonDocument
            {
                ["_id"] = id,
                ["OrganizationId"] = organizationId,
                ["ProjectId"] = projectId,
                ["WorkItemId"] = workItemId
            };
            foreach (var copiedField in copiedFields)
            {
                activity[copiedField] = source.GetValue(copiedField, BsonNull.Value);
            }
            activity["Version"] = 0L;
            await ReplaceMigratedActivityAsync(target, activity, cancellationToken);
        }
    }

    private static async Task ReplaceMigratedActivityAsync(
        IMongoCollection<BsonDocument> collection,
        BsonDocument expected,
        CancellationToken cancellationToken)
    {
        var owner = Builders<BsonDocument>.Filter.Eq("_id", expected["_id"])
            & Builders<BsonDocument>.Filter.Eq("OrganizationId", expected["OrganizationId"])
            & Builders<BsonDocument>.Filter.Eq("ProjectId", expected["ProjectId"])
            & Builders<BsonDocument>.Filter.Eq("WorkItemId", expected["WorkItemId"]);
        try
        {
            await collection.ReplaceOneAsync(
                owner,
                expected,
                new ReplaceOptions { IsUpsert = true },
                cancellationToken);
        }
        catch (MongoWriteException exception)
            when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            throw new InvalidOperationException(
                $"Work item activity '{expected["_id"]}' conflicts with incompatible tenant ownership.",
                exception);
        }
    }

    private async Task<MongoMigrationLedgerDocument> GetOrCreateLedgerAsync(
        string migrationId,
        string checksum,
        CancellationToken cancellationToken)
    {
        var ledger = await LoadLedgerAsync(migrationId, cancellationToken);
        if (ledger is not null)
        {
            EnsureChecksum(ledger, checksum);
            return ledger;
        }

        var now = DateTime.UtcNow;
        ledger = new MongoMigrationLedgerDocument
        {
            Id = migrationId,
            Checksum = checksum,
            State = MongoMigrationStates.Running,
            StartedAt = now,
            UpdatedAt = now
        };
        try
        {
            await Ledgers.InsertOneAsync(ledger, cancellationToken: cancellationToken);
            return ledger;
        }
        catch (MongoWriteException exception) when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            ledger = await LoadLedgerAsync(migrationId, cancellationToken)
                ?? throw new InvalidOperationException("Migration ledger was concurrently created but cannot be loaded.");
            EnsureChecksum(ledger, checksum);
            return ledger;
        }
    }

    private async Task<MongoMigrationLedgerDocument> AcquireLeaseAsync(
        MongoMigrationLedgerDocument ledger,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var filter = Builders<MongoMigrationLedgerDocument>.Filter.Eq(x => x.Id, ledger.Id)
            & Builders<MongoMigrationLedgerDocument>.Filter.Or(
                Builders<MongoMigrationLedgerDocument>.Filter.Eq(x => x.LeaseOwner, _owner),
                Builders<MongoMigrationLedgerDocument>.Filter.Eq(x => x.LeaseOwner, null),
                Builders<MongoMigrationLedgerDocument>.Filter.Lt(x => x.LeaseExpiresAt, now));
        var update = Builders<MongoMigrationLedgerDocument>.Update
            .Set(x => x.LeaseOwner, _owner)
            .Set(x => x.LeaseExpiresAt, now.Add(LeaseDuration))
            .Set(x => x.UpdatedAt, now);
        return await Ledgers.FindOneAndUpdateAsync(
            filter,
            update,
            new FindOneAndUpdateOptions<MongoMigrationLedgerDocument> { ReturnDocument = ReturnDocument.After },
            cancellationToken) ?? ledger;
    }

    private async Task SaveOwnedLedgerAsync(MongoMigrationLedgerDocument ledger, CancellationToken cancellationToken)
    {
        ledger.UpdatedAt = DateTime.UtcNow;
        ledger.LeaseExpiresAt = ledger.UpdatedAt.Add(LeaseDuration);
        var result = await Ledgers.ReplaceOneAsync(
            x => x.Id == ledger.Id && x.LeaseOwner == _owner,
            ledger,
            cancellationToken: cancellationToken);
        if (result.ModifiedCount != 1)
        {
            throw new InvalidOperationException($"Migration lease for '{ledger.Id}' was lost.");
        }
    }

    private async Task SaveAndReleaseOwnedLedgerAsync(
        MongoMigrationLedgerDocument ledger,
        CancellationToken cancellationToken)
    {
        ledger.UpdatedAt = DateTime.UtcNow;
        ReleaseLease(ledger);
        var result = await Ledgers.ReplaceOneAsync(
            x => x.Id == ledger.Id && x.LeaseOwner == _owner,
            ledger,
            cancellationToken: cancellationToken);
        if (result.ModifiedCount != 1)
        {
            throw new InvalidOperationException($"Migration lease for '{ledger.Id}' was lost.");
        }
    }

    private async Task SaveLedgerAsync(MongoMigrationLedgerDocument ledger, CancellationToken cancellationToken)
    {
        ledger.UpdatedAt = DateTime.UtcNow;
        var result = await Ledgers.ReplaceOneAsync(x => x.Id == ledger.Id, ledger, cancellationToken: cancellationToken);
        if (result.MatchedCount != 1)
        {
            throw new InvalidOperationException($"Migration ledger '{ledger.Id}' disappeared.");
        }
    }

    private async Task<MongoMigrationLedgerDocument?> LoadLedgerAsync(string id, CancellationToken cancellationToken) =>
        (MongoMigrationLedgerDocument?)await Ledgers.Find(x => x.Id == id).FirstOrDefaultAsync(cancellationToken);

    private IMongoCollection<MongoMigrationLedgerDocument> Ledgers =>
        mongo.GetCollection<MongoMigrationLedgerDocument>(LedgerCollection, ControlModule);

    private int BatchSize => Math.Clamp(_options.BatchSize, 1, 10_000);
    private int MaxBatches => Math.Clamp(_options.MaxBatchesPerRun, 1, 10_000);
    private static string RankChecksum => Checksum("rank-v2:missing-or-numeric-zero:datetime-offset-array-or-date-or-ticks");
    private static string RefreshSessionChecksum =>
        Checksum("refresh-session-v1:additive:set-on-insert:user-checkpoint:retain-30-days");
    private static string ApiKeyVersionChecksum =>
        Checksum("api-key-version-v2:version-and-scalar-utc-fields:id-checkpoint");
    private static string WorkItemActivityChecksum =>
        Checksum("workitem-activity-v2:six-owned-stores:project-tenant-when-populated:cas-clear:id-checkpoint");
    private static string OrganizationVersionChecksum =>
        Checksum("organization-version-v1:version-one:active-default:id-checkpoint");
    private static string TeamInviteTokenChecksum =>
        Checksum("team-invite-token-v1:expire-hashless-pending:versioned:id-checkpoint");
    private static string ProjectLifecycleChecksum =>
        Checksum("project-lifecycle-v1:version-visibility-catalog-retention-defaults:id-checkpoint");
    private static string WorkflowLifecycleChecksum =>
        Checksum("workflow-lifecycle-v1:published-version-draft-history-issue-scheme:id-checkpoint");
    private static string SprintLifecycleChecksum =>
        Checksum("sprint-lifecycle-v2:project-label-to-planned-aggregate:md5-provider-parity:versioned:id-checkpoint");
    private static string WorkItemTypeSchemaChecksum =>
        Checksum("workitem-type-schema-v1:project-defaults:typed-fields:legacy-types:versioned:id-checkpoint");
    private static string WorkItemGraphChecksum =>
        Checksum("workitem-graph-v1:embedded-relations:canonical-direction:md5-provider-parity:id-checkpoint");

    private static string[] DefaultIssueTypeKeys => ["Epic", "Story", "Task", "Bug", "Subtask"];

    private static BsonArray DefaultIssueTypes() =>
    [
        IssueType("Epic", "Epic", "Epic", 0),
        IssueType("Story", "Story", "Standard", 10),
        IssueType("Task", "Task", "Standard", 20),
        IssueType("Bug", "Bug", "Standard", 30),
        IssueType("Subtask", "Subtask", "Subtask", 40)
    ];

    private static BsonArray DefaultIssueTypeLayouts() => new(
        DefaultIssueTypeKeys.Select(key => new BsonDocument
        {
            ["IssueTypeKey"] = key,
            ["FieldKeys"] = new BsonArray()
        }));

    private static BsonDocument IssueType(
        string key,
        string name,
        string hierarchy,
        int position) => new()
        {
            ["Key"] = key,
            ["Name"] = name,
            ["Description"] = string.Empty,
            ["HierarchyLevel"] = hierarchy,
            ["Active"] = true,
            ["Position"] = position
        };

    private static FilterDefinition<BsonDocument> WorkItemActivityFilter(BsonValue checkpoint)
    {
        var filter = WorkItemActivityVersionFilter();
        if (!checkpoint.IsBsonNull)
        {
            filter &= Builders<BsonDocument>.Filter.Gt("_id", checkpoint);
        }
        return filter;
    }

    private static FilterDefinition<BsonDocument> WorkItemActivityVersionFilter() =>
        Builders<BsonDocument>.Filter.Or(
            Builders<BsonDocument>.Filter.Exists("ActivityStorageVersion", false),
            Builders<BsonDocument>.Filter.Lt("ActivityStorageVersion", 1));

    private static BsonArray ArrayValue(BsonDocument document, string name)
    {
        var value = document.GetValue(name, new BsonArray());
        return value.IsBsonArray ? value.AsBsonArray : new BsonArray();
    }

    private static bool HasMigratableActivities(BsonDocument workItem)
    {
        foreach (var field in new[] { "Comments", "Attachments", "WorkLogs", "Approvals" })
        {
            if (ArrayValue(workItem, field).Any(value =>
                    value.IsBsonDocument
                    && !string.IsNullOrWhiteSpace(StringValue(value.AsBsonDocument, "Id"))))
            {
                return true;
            }
        }

        return ArrayValue(workItem, "StatusHistory").Any(value =>
            value.IsBsonDocument
            && !string.IsNullOrWhiteSpace(StringValue(value.AsBsonDocument, "ToStatus"))
            && TryResolveUtc(value.AsBsonDocument.GetValue("ChangedAt", BsonNull.Value), out _));
    }

    private static string ActivityId(params string[] parts)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\u001f', parts)));
        return Convert.ToHexString(bytes).ToLowerInvariant()[..32];
    }

    private static FilterDefinition<BsonDocument> RankCandidateFilter(BsonValue checkpoint)
    {
        var filter = Builders<BsonDocument>.Filter.Or(
            Builders<BsonDocument>.Filter.Exists("Rank", false),
            Builders<BsonDocument>.Filter.Eq("Rank", 0));
        if (!checkpoint.IsBsonNull)
        {
            filter &= Builders<BsonDocument>.Filter.Gt("_id", checkpoint);
        }

        return filter;
    }

    private static FilterDefinition<BsonDocument> RankCandidateForId(BsonValue id) =>
        Builders<BsonDocument>.Filter.Eq("_id", id)
        & Builders<BsonDocument>.Filter.Or(
            Builders<BsonDocument>.Filter.Exists("Rank", false),
            Builders<BsonDocument>.Filter.Eq("Rank", 0));

    private static FilterDefinition<BsonDocument> RefreshSessionUserFilter(BsonValue checkpoint)
    {
        var filter = Builders<BsonDocument>.Filter.Exists("RefreshTokens.0", true);
        if (!checkpoint.IsBsonNull)
        {
            filter &= Builders<BsonDocument>.Filter.Gt("_id", checkpoint);
        }

        return filter;
    }

    private static FilterDefinition<BsonDocument> ApiKeyVersionFilter(BsonValue checkpoint)
    {
        var filter = Builders<BsonDocument>.Filter.Or(
            Builders<BsonDocument>.Filter.Exists("Version", false),
            Builders<BsonDocument>.Filter.Lte("Version", 0),
            Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Exists("ExpiresAt", true),
                Builders<BsonDocument>.Filter.Exists("ExpiresAtUtc", false)),
            Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Ne("RevokedAt", BsonNull.Value),
                Builders<BsonDocument>.Filter.Exists("RevokedAtUtc", false)));
        if (!checkpoint.IsBsonNull)
        {
            filter &= Builders<BsonDocument>.Filter.Gt("_id", checkpoint);
        }

        return filter;
    }

    private static FilterDefinition<BsonDocument> ApiKeyVersionForId(BsonValue id) =>
        Builders<BsonDocument>.Filter.Eq("_id", id)
        & Builders<BsonDocument>.Filter.Or(
            Builders<BsonDocument>.Filter.Exists("Version", false),
            Builders<BsonDocument>.Filter.Lte("Version", 0),
            Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Exists("ExpiresAt", true),
                Builders<BsonDocument>.Filter.Exists("ExpiresAtUtc", false)),
            Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Ne("RevokedAt", BsonNull.Value),
                Builders<BsonDocument>.Filter.Exists("RevokedAtUtc", false)));

    private static FilterDefinition<BsonDocument> OrganizationVersionFilter(BsonValue checkpoint)
    {
        var filter = Builders<BsonDocument>.Filter.Or(
            Builders<BsonDocument>.Filter.Exists("Version", false),
            Builders<BsonDocument>.Filter.Lte("Version", 0),
            Builders<BsonDocument>.Filter.Type("Version", BsonType.Null));
        if (!checkpoint.IsBsonNull)
        {
            filter &= Builders<BsonDocument>.Filter.Gt("_id", checkpoint);
        }

        return filter;
    }

    private static FilterDefinition<BsonDocument> OrganizationVersionForId(BsonValue id) =>
        Builders<BsonDocument>.Filter.Eq("_id", id)
        & Builders<BsonDocument>.Filter.Or(
            Builders<BsonDocument>.Filter.Exists("Version", false),
            Builders<BsonDocument>.Filter.Lte("Version", 0),
            Builders<BsonDocument>.Filter.Type("Version", BsonType.Null));

    private static FilterDefinition<BsonDocument> LegacyTeamInviteFilter(BsonValue checkpoint)
    {
        var pendingWithoutHash = Builders<BsonDocument>.Filter.ElemMatch(
            "Members",
            Builders<BsonDocument>.Filter.Eq("Status", "Invited")
            & Builders<BsonDocument>.Filter.Or(
                Builders<BsonDocument>.Filter.Exists("InvitationTokenHash", false),
                Builders<BsonDocument>.Filter.Eq("InvitationTokenHash", BsonNull.Value),
                Builders<BsonDocument>.Filter.Eq("InvitationTokenHash", string.Empty)));
        if (!checkpoint.IsBsonNull)
        {
            pendingWithoutHash &= Builders<BsonDocument>.Filter.Gt("_id", checkpoint);
        }

        return pendingWithoutHash;
    }

    private static FilterDefinition<BsonDocument> TeamVersionForId(BsonValue id, long version)
    {
        var versionFilter = version == 0
            ? Builders<BsonDocument>.Filter.Or(
                Builders<BsonDocument>.Filter.Exists("Version", false),
                Builders<BsonDocument>.Filter.Eq("Version", 0),
                Builders<BsonDocument>.Filter.Type("Version", BsonType.Null))
            : Builders<BsonDocument>.Filter.Eq("Version", version);
        return Builders<BsonDocument>.Filter.Eq("_id", id) & versionFilter;
    }

    private static FilterDefinition<BsonDocument> ProjectLifecycleFilter(BsonValue checkpoint)
    {
        var filter = Builders<BsonDocument>.Filter.Or(
            Builders<BsonDocument>.Filter.Exists("Version", false),
            Builders<BsonDocument>.Filter.Lte("Version", 0),
            Builders<BsonDocument>.Filter.Type("Version", BsonType.Null),
            Builders<BsonDocument>.Filter.Exists("Visibility", false),
            Builders<BsonDocument>.Filter.Exists("Templates", false),
            Builders<BsonDocument>.Filter.Exists("Components", false),
            Builders<BsonDocument>.Filter.Exists("Versions", false),
            Builders<BsonDocument>.Filter.Exists("Releases", false),
            Builders<BsonDocument>.Filter.Exists("Milestones", false),
            Builders<BsonDocument>.Filter.Exists("ArchivedAt", false),
            Builders<BsonDocument>.Filter.Exists("RetainUntil", false));
        if (!checkpoint.IsBsonNull)
        {
            filter &= Builders<BsonDocument>.Filter.Gt("_id", checkpoint);
        }

        return filter;
    }

    private static FilterDefinition<BsonDocument> WorkflowLifecycleFilter(BsonValue checkpoint)
    {
        var filter = Builders<BsonDocument>.Filter.Or(
            Builders<BsonDocument>.Filter.Exists("PublishedVersion", false),
            Builders<BsonDocument>.Filter.Exists("IssueTypeSchemes", false),
            Builders<BsonDocument>.Filter.Exists("Draft", false),
            Builders<BsonDocument>.Filter.Exists("PublishedVersions", false));
        if (!checkpoint.IsBsonNull)
        {
            filter &= Builders<BsonDocument>.Filter.Gt("_id", checkpoint);
        }

        return filter;
    }

    private static FilterDefinition<BsonDocument> SprintLifecycleFilter(BsonValue checkpoint)
    {
        var filter = Builders<BsonDocument>.Filter.Exists("SprintId", true)
            & Builders<BsonDocument>.Filter.Ne("SprintId", BsonNull.Value)
            & Builders<BsonDocument>.Filter.Ne("SprintId", string.Empty)
            & Builders<BsonDocument>.Filter.Exists("SprintLifecycleMigratedBy", false);
        if (!checkpoint.IsBsonNull)
        {
            filter &= Builders<BsonDocument>.Filter.Gt("_id", checkpoint);
        }

        return filter;
    }

    private static FilterDefinition<BsonDocument> WorkItemTypeSchemaFilter(BsonValue checkpoint)
    {
        var filter = Builders<BsonDocument>.Filter.Exists("IssueTypeSchemaVersion", false)
            & Builders<BsonDocument>.Filter.Exists("CustomFields", false);
        if (!checkpoint.IsBsonNull)
        {
            filter &= Builders<BsonDocument>.Filter.Gt("_id", checkpoint);
        }

        return filter;
    }

    private static FilterDefinition<BsonDocument> WorkItemGraphFilter(BsonValue checkpoint)
    {
        var filter = Builders<BsonDocument>.Filter.Exists("Relations.0", true);
        if (!checkpoint.IsBsonNull)
        {
            filter &= Builders<BsonDocument>.Filter.Gt("_id", checkpoint);
        }

        return filter;
    }

    private static string? NormalizeGraphRelationType(string? relationType) =>
        relationType?.Trim().ToLowerInvariant() switch
        {
            "blocks" => "Blocks",
            "blockedby" or "blocked-by" => "BlockedBy",
            "relatesto" or "relates-to" => "RelatesTo",
            "duplicates" => "Duplicates",
            _ => null
        };

    private static string WorkItemRelationEdgeId(
        string projectId,
        string sourceWorkItemId,
        string targetWorkItemId,
        string relationType)
    {
        var value = Encoding.UTF8.GetBytes(
            $"{projectId}\n{sourceWorkItemId}\n{targetWorkItemId}\n{relationType}");
        return Convert.ToHexString(MD5.HashData(value)).ToLowerInvariant();
    }

    private static string LegacySprintId(string projectId, string sprintId)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(projectId + ":" + sprintId));
        return "legacy-" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static FilterDefinition<BsonDocument> ProjectVersionForId(BsonValue id, long version)
    {
        var versionFilter = version == 0
            ? Builders<BsonDocument>.Filter.Or(
                Builders<BsonDocument>.Filter.Exists("Version", false),
                Builders<BsonDocument>.Filter.Eq("Version", 0),
                Builders<BsonDocument>.Filter.Type("Version", BsonType.Null))
            : Builders<BsonDocument>.Filter.Eq("Version", version);
        return Builders<BsonDocument>.Filter.Eq("_id", id) & versionFilter;
    }

    private static FilterDefinition<BsonDocument> WorkflowVersionForId(BsonValue id, long version)
    {
        var versionFilter = version == 0
            ? Builders<BsonDocument>.Filter.Or(
                Builders<BsonDocument>.Filter.Exists("Version", false),
                Builders<BsonDocument>.Filter.Eq("Version", 0))
            : Builders<BsonDocument>.Filter.Eq("Version", version);
        return Builders<BsonDocument>.Filter.Eq("_id", id) & versionFilter;
    }

    private static bool TryCreateRefreshSession(
        BsonValue value,
        string userId,
        string organizationId,
        out BsonDocument session)
    {
        session = null!;
        if (!value.IsBsonDocument
            || string.IsNullOrWhiteSpace(userId)
            || string.IsNullOrWhiteSpace(organizationId))
        {
            return false;
        }

        var token = value.AsBsonDocument;
        var sessionId = StringValue(token, "SessionId");
        var tokenHash = StringValue(token, "TokenHash");
        if (string.IsNullOrWhiteSpace(sessionId)
            || string.IsNullOrWhiteSpace(tokenHash)
            || !TryResolveUtc(token.GetValue("ExpiresAt", BsonNull.Value), out var expiresAt))
        {
            return false;
        }

        var createdAt = token.GetValue("CreatedAt", token["ExpiresAt"]);
        var revokedValue = token.GetValue("RevokedAt", BsonNull.Value);
        BsonValue revokedAtUtc = BsonNull.Value;
        var retainBase = expiresAt;
        if (TryResolveUtc(revokedValue, out var revokedAt))
        {
            revokedAtUtc = revokedAt;
            if (revokedAt > retainBase)
            {
                retainBase = revokedAt;
            }
        }

        session = new BsonDocument
        {
            ["_id"] = sessionId,
            ["UserId"] = userId,
            ["OrganizationId"] = organizationId,
            ["TokenHash"] = tokenHash,
            ["CreatedAt"] = createdAt,
            ["ExpiresAt"] = token["ExpiresAt"],
            ["ExpiresAtUtc"] = expiresAt,
            ["RevokedAt"] = revokedValue,
            ["RevokedAtUtc"] = revokedAtUtc,
            ["ReplacedBySessionId"] = BsonNull.Value,
            ["RetainUntilUtc"] = retainBase.AddDays(30),
            ["Version"] = 1L
        };
        return true;
    }

    private static async Task EnsureRefreshSessionMatchesAsync(
        IMongoCollection<BsonDocument> sessions,
        BsonDocument expected,
        CancellationToken cancellationToken)
    {
        var filter = Builders<BsonDocument>.Filter.Or(
            Builders<BsonDocument>.Filter.Eq("_id", expected["_id"]),
            Builders<BsonDocument>.Filter.Eq("TokenHash", expected["TokenHash"]));
        var actual = await sessions.Find(filter).FirstOrDefaultAsync(cancellationToken);
        if (actual is null
            || actual.GetValue("_id", BsonNull.Value) != expected["_id"]
            || actual.GetValue("UserId", BsonNull.Value) != expected["UserId"]
            || actual.GetValue("OrganizationId", BsonNull.Value) != expected["OrganizationId"]
            || actual.GetValue("TokenHash", BsonNull.Value) != expected["TokenHash"])
        {
            throw new InvalidOperationException(
                $"Refresh session '{expected["_id"]}' conflicts with incompatible stored ownership or token data.");
        }
    }

    private static string? StringValue(BsonDocument document, string name)
    {
        var value = document.GetValue(name, BsonNull.Value);
        return value.IsString ? value.AsString : null;
    }

    private static bool TryResolveUtc(BsonValue value, out DateTime utc)
    {
        utc = default;
        try
        {
            utc = value.BsonType switch
            {
                BsonType.DateTime => value.ToUniversalTime(),
                BsonType.Int64 => new DateTime(value.AsInt64, DateTimeKind.Utc),
                BsonType.Int32 => new DateTime(value.AsInt32, DateTimeKind.Utc),
                BsonType.Array when value.AsBsonArray.Count > 0 =>
                    new DateTime(NumericTicks(value.AsBsonArray[0]), DateTimeKind.Utc),
                BsonType.Document when value.AsBsonDocument.TryGetValue("Ticks", out var ticks) =>
                    new DateTime(NumericTicks(ticks), DateTimeKind.Utc),
                _ => default
            };
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentOutOfRangeException)
        {
            utc = default;
        }

        return utc != default;
    }

    public static bool TryResolveRank(BsonValue createdAt, out long rank)
    {
        rank = 0;
        try
        {
            rank = createdAt.BsonType switch
            {
                BsonType.DateTime => DateTimeOffset.FromUnixTimeMilliseconds(createdAt.AsBsonDateTime.MillisecondsSinceEpoch).UtcTicks,
                BsonType.Int64 => createdAt.AsInt64,
                BsonType.Int32 => createdAt.AsInt32,
                BsonType.Array when createdAt.AsBsonArray.Count > 0 => NumericTicks(createdAt.AsBsonArray[0]),
                BsonType.Document => ResolveDocumentTicks(createdAt.AsBsonDocument),
                _ => 0
            };
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentOutOfRangeException or FormatException)
        {
            rank = 0;
        }

        return rank > 0 && rank <= DateTimeOffset.MaxValue.UtcTicks;
    }

    private static long ResolveDocumentTicks(BsonDocument document)
    {
        if (document.TryGetValue("Ticks", out var ticks)) return NumericTicks(ticks);
        if (document.TryGetValue("DateTime", out var dateTime) && dateTime.IsBsonDateTime)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(dateTime.AsBsonDateTime.MillisecondsSinceEpoch).UtcTicks;
        }

        return 0;
    }

    private static long NumericTicks(BsonValue value) => value.BsonType switch
    {
        BsonType.Int64 => value.AsInt64,
        BsonType.Int32 => value.AsInt32,
        _ => 0
    };

    private static string BackupId(BsonValue id) => $"{RankMigrationId}:{id.BsonType}:{id}";

    private static string SerializeIndex(MongoIndexSpecification index) =>
        $"{index.Module}:{index.Collection}:{index.Name}:{index.Keys}:{index.Unique}:{index.CaseInsensitive}:{index.ExpireAfter}:{index.PartialFilter}";

    private static string Checksum(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static void EnsureChecksum(MongoMigrationLedgerDocument ledger, string expected)
    {
        if (!string.Equals(ledger.Checksum, expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Migration '{ledger.Id}' checksum changed after it was recorded.");
        }
    }

    private static void ReleaseLease(MongoMigrationLedgerDocument ledger)
    {
        ledger.LeaseOwner = null;
        ledger.LeaseExpiresAt = null;
    }

    private static MongoMigrationOutcome ToOutcome(MongoMigrationLedgerDocument ledger, string status) =>
        new(ledger.Id, status, ledger.Examined, ledger.Changed, ledger.Skipped);

    private void ValidateOptions()
    {
        if (_options.BatchSize <= 0) throw new InvalidOperationException("MongoMigrations:BatchSize must be positive.");
        if (_options.MaxBatchesPerRun <= 0) throw new InvalidOperationException("MongoMigrations:MaxBatchesPerRun must be positive.");
        if (_options.RunDataMigrations && !_options.DryRun && _options.BatchSize > 10_000)
        {
            logger.LogWarning("Mongo migration batch size was capped at 10000 from {ConfiguredBatchSize}", _options.BatchSize);
        }
    }
}
