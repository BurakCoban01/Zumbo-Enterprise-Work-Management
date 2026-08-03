using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Persistence.PostgreSql;

internal static class PostgreSqlDocumentCatalog
{
    public static IReadOnlyList<PostgreSqlDocumentStorage> BuiltInStorages { get; } =
    [
        new("identity", "users"),
        new("identity", "api_keys"),
        new("identity", "identity_roles"),
        new("organizations", "organizations"),
        new("teams", "teams"),
        new("projects", "projects"),
        new("boards", "boards"),
        new("work_items", "work_items"),
        new("workflows", "workflow_definitions"),
        new("notifications", "notifications"),
        new("notifications", "notification_preferences"),
        new("audit", "audit_logs")
    ];
}
