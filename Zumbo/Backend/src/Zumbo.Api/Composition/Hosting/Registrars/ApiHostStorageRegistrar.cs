using System.Net;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Runtime;
using Zumbo.BuildingBlocks.Application.Search;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.BuildingBlocks.Infrastructure.Concurrency;
using Zumbo.BuildingBlocks.Infrastructure.Messaging;
using Zumbo.BuildingBlocks.Infrastructure.Runtime;
using Zumbo.BuildingBlocks.Infrastructure.Search;
using Zumbo.BuildingBlocks.Infrastructure.Security;
using Zumbo.BuildingBlocks.Infrastructure.Storage;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Notifications;
using Zumbo.Modules.WorkItems;
using Zumbo.Persistence.PostgreSql;
using Zumbo.SharedKernel;
using MongoDurableTransactionRunner = Zumbo.BuildingBlocks.Infrastructure.Persistence.MongoDurableTransactionRunner;
using MongoTransactionContext = Zumbo.BuildingBlocks.Infrastructure.Persistence.MongoTransactionContext;

internal static class ApiHostStorageRegistrar
{
    internal static (string Provider, bool IsWorkerRole) ConfigureRuntimeProviders(WebApplicationBuilder builder)
{

        var runtimeRole = builder.Configuration.GetValue<string>("Runtime:Role") ?? "Api";

        var isWorkerRole = runtimeRole.Equals("Worker", StringComparison.OrdinalIgnoreCase);

        var searchProvider = builder.Configuration.GetValue<string>("Search:Provider") ?? "InMemory";

        if (searchProvider.Equals("OpenSearch", StringComparison.OrdinalIgnoreCase))
        {
            builder.Services.AddHttpClient("WorkItemOpenSearch", client =>
                client.Timeout = Timeout.InfiniteTimeSpan);
            builder.Services.AddSingleton<IWorkItemSearchIndex>(provider =>
                new OpenSearchWorkItemSearchIndex(
                    provider.GetRequiredService<IHttpClientFactory>().CreateClient("WorkItemOpenSearch"),
                    provider.GetRequiredService<IOptions<OpenSearchOptions>>(),
                    provider.GetRequiredService<IExternalDependencyPolicyProvider>()));
            if (!isWorkerRole)
            {
                builder.Services.AddHostedService<SearchIndexInitializer>();
            }
        }
        else
        {
            builder.Services.AddSingleton<IWorkItemSearchIndex, InMemoryWorkItemSearchIndex>();
        }


        var provider = builder.Configuration.GetValue<string>("Persistence:Provider") ?? "InMemory";

        builder.Services.AddSingleton<IDurableMessageJitter, RandomDurableMessageJitter>();

        if (provider.Equals("Mongo", StringComparison.OrdinalIgnoreCase))
        {
            builder.Services.Configure<MongoMigrationOptions>(builder.Configuration.GetSection("MongoMigrations"));
            builder.Services.AddSingleton<MongoMigrationRunner>();
            builder.Services.AddSingleton<
                Zumbo.BuildingBlocks.Infrastructure.Persistence.IMongoDbService,
                Zumbo.BuildingBlocks.Infrastructure.Persistence.MongoDbService>();
            builder.Services.AddScoped<MongoTransactionContext>();
            builder.Services.AddScoped<IDurableTransactionRunner, MongoDurableTransactionRunner>();
            builder.Services.AddScoped<IDurableEventOutbox, MongoDurableEventOutbox>();
            builder.Services.AddScoped<IDurableEventInbox, MongoDurableEventInbox>();
            builder.Services.AddScoped(
                typeof(IDocumentRepository<>),
                typeof(Zumbo.BuildingBlocks.Infrastructure.Persistence.MongoRepository<>));
            if (!isWorkerRole)
            {
                builder.Services.AddHostedService<MongoIndexInitializer>();
            }
        }
        else if (provider.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase))
        {
            builder.Services.AddZumboPostgreSql(options =>
            {
                options.ConnectionString = builder.Configuration["PostgreSql:ConnectionString"]
                    ?? builder.Configuration.GetConnectionString("PostgreSql")
                    ?? string.Empty;
                options.CommandTimeoutSeconds = builder.Configuration.GetValue("PostgreSql:CommandTimeoutSeconds", 30);
                options.ConnectionTimeoutSeconds = builder.Configuration.GetValue("PostgreSql:ConnectionTimeoutSeconds", 5);
                options.MinimumPoolSize = builder.Configuration.GetValue("PostgreSql:MinimumPoolSize", 0);
                options.MaximumPoolSize = builder.Configuration.GetValue("PostgreSql:MaximumPoolSize", 100);
                options.MapDocument<Zumbo.Modules.Identity.UserDocument>("identity", "users");
                options.MapDocument<Zumbo.Modules.Identity.RefreshSessionDocument>("identity", "refresh_sessions");
                options.MapDocument<Zumbo.Modules.Identity.ApiKeyDocument>("identity", "api_keys");
                options.MapDocument<Zumbo.Modules.Identity.IdentityRoleDocument>("identity", "identity_roles");
                options.MapDocument<Zumbo.Modules.Identity.PrivacyWorkflowDocument>("identity", "privacy_workflows");
                options.MapDocument<Zumbo.Modules.Organizations.OrganizationDocument>("organizations", "organizations");
                options.MapDocument<Zumbo.Modules.Teams.TeamDocument>("teams", "teams");
                options.MapDocument<Zumbo.Modules.Projects.ProjectDocument>("projects", "projects");
                options.MapDocument<Zumbo.Modules.Projects.PortfolioDocument>("projects", "portfolios");
                options.MapDocument<Zumbo.Modules.Projects.GoalDocument>("projects", "goals");
                options.MapDocument<Zumbo.Modules.Projects.KnowledgeDocument>("projects", "knowledge_documents");
                options.MapDocument<Zumbo.Modules.Boards.BoardDocument>("boards", "boards");
                options.MapDocument<Zumbo.Modules.WorkItems.WorkItemDocument>("work_items", "work_items");
                options.MapDocument<Zumbo.Modules.WorkItems.WorkItemTypeSchemaDocument>("work_items", "work_item_type_schemas");
                options.MapDocument<Zumbo.Modules.WorkItems.WorkItemRelationEdgeDocument>("work_items", "work_item_relation_edges");
                options.MapDocument<Zumbo.Modules.WorkItems.WorkItemCollaborationDocument>("work_items", "work_item_collaborations");
                options.MapDocument<Zumbo.Modules.WorkItems.WorkItemEventActivityDocument>("work_items", "work_item_event_activities");
                options.MapDocument<Zumbo.Modules.WorkItems.WorkItemTemplateDocument>("work_items", "work_item_templates");
                options.MapDocument<Zumbo.Modules.WorkItems.WorkItemRecurrenceDocument>("work_items", "work_item_recurrences");
                options.MapDocument<Zumbo.Modules.WorkItems.WorkItemRecurrenceOccurrenceDocument>("work_items", "work_item_recurrence_occurrences");
                options.MapDocument<Zumbo.Modules.WorkItems.WorkItemBulkJobDocument>("work_items", "work_item_bulk_jobs");
                options.MapDocument<Zumbo.Modules.WorkItems.WorkItemBulkJobItemDocument>("work_items", "work_item_bulk_job_items");
                options.MapDocument<Zumbo.Modules.WorkItems.IntakeFormDocument>("work_items", "intake_forms");
                options.MapDocument<Zumbo.Modules.WorkItems.IntakeFormVersionDocument>("work_items", "intake_form_versions");
                options.MapDocument<Zumbo.Modules.WorkItems.IntakeSubmissionDocument>("work_items", "intake_submissions");
                options.MapDocument<Zumbo.Modules.WorkItems.DashboardDocument>("work_items", "dashboards");
                options.MapDocument<Zumbo.Modules.WorkItems.CapacityPlanDocument>("work_items", "capacity_plans");
                options.MapDocument<Zumbo.Modules.WorkItems.DevelopmentConnectionDocument>("work_items", "development_connections");
                options.MapDocument<Zumbo.Modules.WorkItems.DevelopmentRepositoryMappingDocument>("work_items", "development_repository_mappings");
                options.MapDocument<Zumbo.Modules.WorkItems.WorkItemDevelopmentLinkDocument>("work_items", "work_item_development_links");
                options.MapDocument<Zumbo.Modules.WorkItems.DevelopmentWebhookReceiptDocument>("work_items", "development_webhook_receipts");
                options.MapDocument<Zumbo.Modules.WorkItems.WebhookSubscriptionDocument>("work_items", "webhook_subscriptions");
                options.MapDocument<Zumbo.Modules.WorkItems.WebhookDeliveryDocument>("work_items", "webhook_deliveries");
                options.MapDocument<Zumbo.Modules.WorkItems.BoardColumnWipProjectionDocument>("work_items", "board_column_wip_projections");
                options.MapDocument<Zumbo.Modules.WorkItems.SprintDocument>("work_items", "sprints");
                options.MapDocument<Zumbo.Modules.WorkItems.SprintScopeSnapshotDocument>("work_items", "sprint_scope_snapshots");
                options.MapDocument<Zumbo.Modules.WorkItems.SprintCompletionSnapshotDocument>("work_items", "sprint_completion_snapshots");
                options.MapDocument<Zumbo.Modules.WorkItems.WorkItemCommentActivityDocument>("work_items", "work_item_comments");
                options.MapDocument<Zumbo.Modules.WorkItems.WorkItemCommentRevisionActivityDocument>("work_items", "work_item_comment_revisions");
                options.MapDocument<Zumbo.Modules.WorkItems.WorkItemAttachmentActivityDocument>("work_items", "work_item_attachments");
                options.MapDocument<Zumbo.Modules.WorkItems.WorkItemWorkLogActivityDocument>("work_items", "work_item_work_logs");
                options.MapDocument<Zumbo.Modules.WorkItems.WorkItemApprovalActivityDocument>("work_items", "work_item_approvals");
                options.MapDocument<Zumbo.Modules.WorkItems.WorkItemTimelineActivityDocument>("work_items", "work_item_timeline");
                options.MapDocument<Zumbo.Modules.Workflows.WorkflowDefinitionDocument>("workflows", "workflow_definitions");
                options.MapDocument<Zumbo.Modules.Workflows.AutomationRuleDocument>("workflows", "automation_rules");
                options.MapDocument<Zumbo.Modules.Workflows.AutomationRunDocument>("workflows", "automation_runs");
                options.MapDocument<Zumbo.Modules.Notifications.NotificationDocument>("notifications", "notifications");
                options.MapDocument<Zumbo.Modules.Notifications.NotificationPreferenceDocument>("notifications", "notification_preferences");
                options.MapDocument<Zumbo.Modules.Audit.AuditLogDocument>("audit", "audit_logs");
            });
        }
        else if (provider.Equals("InMemory", StringComparison.OrdinalIgnoreCase))
        {
            builder.Services.AddSingleton(
                typeof(IDocumentRepository<>),
                typeof(Zumbo.BuildingBlocks.Infrastructure.Persistence.InMemoryDocumentRepository<>));
            builder.Services.AddSingleton<IDurableTransactionRunner, InMemoryDurableTransactionRunner>();
            builder.Services.AddSingleton<IDurableEventOutbox, InMemoryDurableEventOutbox>();
            builder.Services.AddSingleton<IDurableEventInbox, InMemoryDurableEventInbox>();
        }
        else
        {
            throw new InvalidOperationException(
                $"Unsupported persistence provider '{provider}'. Expected InMemory, Mongo, or PostgreSql.");
        }

return (provider, isWorkerRole);
}
    internal static void ConfigureCoreServicesAndStorage(WebApplicationBuilder builder)
{

        builder.Services.AddSingleton<IClock, Zumbo.BuildingBlocks.Infrastructure.Runtime.SystemClock>();

        var readModelCacheProvider = builder.Configuration.GetValue<string>("ReadModelCache:Provider") ?? "InMemory";

        if (readModelCacheProvider.Equals("Redis", StringComparison.OrdinalIgnoreCase))
        {
            builder.Services.AddSingleton<IWorkItemReadModelCache, RedisWorkItemReadModelCache>();
        }
        else
        {
            builder.Services.AddSingleton<IWorkItemReadModelCache, InMemoryWorkItemReadModelCache>();
        }

        builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();

        builder.Services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();

        builder.Services.AddSingleton<ITokenIssuer, JwtTokenIssuer>();


        var storageProvider = StorageConfiguration.GetValidatedProvider(builder.Configuration);

        if (storageProvider.Equals("Minio", StringComparison.OrdinalIgnoreCase))
        {
            builder.Services.AddSingleton<IFileStorage, MinioFileStorage>();
        }
        else if (storageProvider.Equals("Local", StringComparison.OrdinalIgnoreCase))
        {
            builder.Services.AddSingleton<IFileStorage, LocalFileStorage>();
        }


        var scannerProvider = builder.Configuration.GetValue<string>("AttachmentSecurity:ScannerProvider") ?? "PolicyOnly";

        if (scannerProvider.Equals("ClamAv", StringComparison.Ordinal))
        {
            builder.Services.AddSingleton<IAttachmentMalwareScanner, ClamAvAttachmentMalwareScanner>();
        }
        else
        {
            builder.Services.AddSingleton<IAttachmentMalwareScanner, PolicyOnlyAttachmentMalwareScanner>();
        }

}
}
