using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

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
