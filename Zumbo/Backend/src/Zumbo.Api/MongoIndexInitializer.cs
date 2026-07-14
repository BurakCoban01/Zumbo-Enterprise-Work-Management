using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.Audit;
using Zumbo.Modules.Boards;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Notifications;
using Zumbo.Modules.Projects;
using Zumbo.Modules.Teams;
using Zumbo.Modules.WorkItems;

public sealed class MongoIndexInitializer(IMongoDbService mongo, ILogger<MongoIndexInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var caseInsensitiveUnique = new CreateIndexOptions
        {
            Unique = true,
            Collation = new Collation("en", strength: CollationStrength.Secondary)
        };

        var users = mongo.GetCollection<UserDocument>("users");
        await users.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<UserDocument>(Builders<UserDocument>.IndexKeys.Ascending(x => x.Username), caseInsensitiveUnique),
                new CreateIndexModel<UserDocument>(Builders<UserDocument>.IndexKeys.Ascending(x => x.Email), caseInsensitiveUnique),
                new CreateIndexModel<UserDocument>(Builders<UserDocument>.IndexKeys.Ascending(x => x.OrganizationId))
            ],
            cancellationToken);

        var roles = mongo.GetCollection<IdentityRoleDocument>("identityroles");
        await roles.Indexes.CreateOneAsync(
            new CreateIndexModel<IdentityRoleDocument>(
                Builders<IdentityRoleDocument>.IndexKeys
                    .Ascending(x => x.OrganizationId)
                    .Ascending(x => x.Name),
                caseInsensitiveUnique),
            cancellationToken: cancellationToken);

        var apiKeys = mongo.GetCollection<ApiKeyDocument>("apikeys");
        try
        {
            await apiKeys.Indexes.DropOneAsync(
                "UserId_1_RevokedAt_1_CreatedAt_-1",
                cancellationToken);
        }
        catch (MongoCommandException exception) when (exception.CodeName == "IndexNotFound")
        {
            // The incompatible legacy index is absent on new databases.
        }
        await apiKeys.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<ApiKeyDocument>(
                    Builders<ApiKeyDocument>.IndexKeys
                        .Ascending(x => x.UserId)
                        .Ascending(x => x.RevokedAt)),
                new CreateIndexModel<ApiKeyDocument>(
                    Builders<ApiKeyDocument>.IndexKeys
                        .Ascending(x => x.UserId)
                        .Descending(x => x.CreatedAt)),
                new CreateIndexModel<ApiKeyDocument>(
                    Builders<ApiKeyDocument>.IndexKeys.Ascending(x => x.ExpiresAt))
            ],
            cancellationToken);

        var projects = mongo.GetCollection<ProjectDocument>("projects");
        await projects.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<ProjectDocument>(
                    Builders<ProjectDocument>.IndexKeys.Ascending(x => x.OrganizationId).Ascending(x => x.Key),
                    caseInsensitiveUnique),
                new CreateIndexModel<ProjectDocument>(
                    Builders<ProjectDocument>.IndexKeys.Ascending(x => x.OrganizationId).Ascending(x => x.Archived))
            ],
            cancellationToken);

        var teams = mongo.GetCollection<TeamDocument>("teams");
        await teams.Indexes.CreateOneAsync(
            new CreateIndexModel<TeamDocument>(
                Builders<TeamDocument>.IndexKeys
                    .Ascending(x => x.OrganizationId)
                    .Ascending(x => x.Archived)
                    .Ascending(x => x.Name)),
            cancellationToken: cancellationToken);

        var boards = mongo.GetCollection<BoardDocument>("boards");
        await boards.Indexes.CreateOneAsync(
            new CreateIndexModel<BoardDocument>(
                Builders<BoardDocument>.IndexKeys
                    .Ascending(x => x.ProjectId)
                    .Ascending(x => x.Archived)
                    .Ascending(x => x.Name)),
            cancellationToken: cancellationToken);

        var workItems = mongo.GetCollection<WorkItemDocument>("workitems");
        var missingRankUpdate = new PipelineUpdateDefinition<WorkItemDocument>(
        new BsonDocument[]
        {
            new BsonDocument("$set", new BsonDocument(
                nameof(WorkItemDocument.Rank),
                new BsonDocument("$arrayElemAt", new BsonArray { "$CreatedAt", 0 })))
        });
        await workItems.UpdateManyAsync(
            Builders<WorkItemDocument>.Filter.Or(
                Builders<WorkItemDocument>.Filter.Exists(nameof(WorkItemDocument.Rank), false),
                Builders<WorkItemDocument>.Filter.Eq(x => x.Rank, 0)),
            missingRankUpdate,
            cancellationToken: cancellationToken);
        await workItems.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<WorkItemDocument>(
                    Builders<WorkItemDocument>.IndexKeys
                        .Ascending(x => x.ProjectId)
                        .Ascending(x => x.Archived)
                        .Descending(x => x.CreatedAt)),
                new CreateIndexModel<WorkItemDocument>(
                    Builders<WorkItemDocument>.IndexKeys
                        .Ascending(x => x.BoardId)
                        .Ascending(x => x.ColumnId)
                        .Ascending(x => x.Archived)
                        .Ascending(x => x.Rank)),
                new CreateIndexModel<WorkItemDocument>(
                    Builders<WorkItemDocument>.IndexKeys
                        .Ascending(x => x.ProjectId)
                        .Ascending(x => x.Archived)
                        .Ascending(x => x.Status)
                        .Ascending(x => x.Rank)),
                new CreateIndexModel<WorkItemDocument>(
                    Builders<WorkItemDocument>.IndexKeys
                        .Ascending(x => x.ProjectId)
                        .Ascending(x => x.Archived)
                        .Ascending(x => x.Rank)),
                new CreateIndexModel<WorkItemDocument>(
                    Builders<WorkItemDocument>.IndexKeys
                        .Ascending(x => x.ParentId)
                        .Ascending(x => x.Archived)),
                new CreateIndexModel<WorkItemDocument>(
                    Builders<WorkItemDocument>.IndexKeys
                        .Ascending(x => x.ProjectId)
                        .Ascending(x => x.Archived)
                        .Ascending(x => x.AssigneeUserId)),
                new CreateIndexModel<WorkItemDocument>(
                    Builders<WorkItemDocument>.IndexKeys
                        .Ascending(x => x.ProjectId)
                        .Ascending(x => x.Archived)
                        .Ascending(x => x.DueDate)),
                new CreateIndexModel<WorkItemDocument>(
                    Builders<WorkItemDocument>.IndexKeys
                        .Ascending(x => x.AssigneeUserId)
                        .Ascending(x => x.Archived)),
                new CreateIndexModel<WorkItemDocument>(
                    Builders<WorkItemDocument>.IndexKeys
                        .Ascending(x => x.DueDate)
                        .Ascending(x => x.Archived)),
                new CreateIndexModel<WorkItemDocument>(
                    Builders<WorkItemDocument>.IndexKeys.Text(x => x.Title).Text(x => x.Description))
            ],
            cancellationToken);

        var auditLogs = mongo.GetCollection<AuditLogDocument>("auditlogs");
        await auditLogs.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<AuditLogDocument>(
                    Builders<AuditLogDocument>.IndexKeys
                        .Ascending(x => x.EntityType)
                        .Ascending(x => x.EntityId)
                        .Descending(x => x.CreatedAt)),
                new CreateIndexModel<AuditLogDocument>(
                    Builders<AuditLogDocument>.IndexKeys
                        .Ascending(x => x.ActorUserId)
                        .Descending(x => x.CreatedAt)),
                new CreateIndexModel<AuditLogDocument>(
                    Builders<AuditLogDocument>.IndexKeys
                        .Ascending(x => x.Action)
                        .Descending(x => x.CreatedAt))
            ],
            cancellationToken);

        var notifications = mongo.GetCollection<NotificationDocument>("notifications");
        await notifications.Indexes.CreateOneAsync(
            new CreateIndexModel<NotificationDocument>(
                Builders<NotificationDocument>.IndexKeys
                    .Ascending(x => x.UserId)
                    .Ascending(x => x.Read)
                    .Descending(x => x.CreatedAt)),
            cancellationToken: cancellationToken);

        logger.LogInformation("MongoDB indexes were verified successfully");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
