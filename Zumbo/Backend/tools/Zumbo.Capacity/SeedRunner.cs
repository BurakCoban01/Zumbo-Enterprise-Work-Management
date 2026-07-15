using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Search;
using Zumbo.BuildingBlocks.Infrastructure.Security;
using Zumbo.Modules.Audit;
using Zumbo.Modules.Boards;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.Projects;
using Zumbo.Modules.WorkItems;

namespace Zumbo.Capacity;

internal sealed class SeedRunner(string mongoConnectionString, string openSearchBaseUrl)
{
    private const int DocumentBatchSize = 2_000;
    private const int AuditBatchSize = 5_000;
    private readonly MongoClient _mongo = new(mongoConnectionString);
    private readonly HttpClient _search = new() { BaseAddress = new Uri(openSearchBaseUrl.TrimEnd('/') + "/"), Timeout = TimeSpan.FromMinutes(5) };

    public async Task<SeedResult> RunAsync(CapacityProfile profile, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        Console.Error.WriteLine($"{profile.Name} profili temizleniyor...");
        await DeleteExistingAsync(profile, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var passwordHash = new Pbkdf2PasswordHasher().Hash("P@ssword123");
        var users = BuildUsers(profile, passwordHash, now);
        var organizations = BuildOrganizations(profile, now);
        var projects = BuildProjects(profile, now);
        var boards = BuildBoards(profile, now);

        await InsertAsync(Collection<OrganizationDocument>("ZumboOrganizations", "organizations"), organizations, cancellationToken);
        await InsertAsync(Collection<UserDocument>("ZumboIdentity", "users"), users, cancellationToken);
        await InsertAsync(Collection<ProjectDocument>("ZumboProjects", "projects"), projects, cancellationToken);
        await InsertAsync(Collection<BoardDocument>("ZumboBoards", "boards"), boards, cancellationToken);

        await SetRefreshIntervalAsync("-1", cancellationToken);
        try
        {
            await SeedWorkItemsAsync(profile, now, cancellationToken);
        }
        finally
        {
            await SetRefreshIntervalAsync("1s", cancellationToken);
        }

        await SeedAuditAsync(profile, now, cancellationToken);
        using var refreshResponse = await _search.PostAsync("zumbo-work-items/_refresh", null, cancellationToken);
        refreshResponse.EnsureSuccessStatusCode();

        var result = new SeedResult(
            profile.Name,
            profile.Prefix,
            organizations.Count,
            users.Count,
            projects.Count,
            boards.Count,
            profile.WorkItemCount,
            profile.ActivityEventCount,
            stopwatch.ElapsedMilliseconds,
            CapacityIds.Username(profile, 0));
        Console.Error.WriteLine($"{profile.Name} profili {stopwatch.Elapsed} içinde tamamlandı.");
        return result;
    }

    public async Task<object> CleanAsync(CapacityProfile profile, CancellationToken cancellationToken)
    {
        await DeleteExistingAsync(profile, cancellationToken);
        return new { profile = profile.Name, prefix = profile.Prefix, deleted = true };
    }

    private async Task DeleteExistingAsync(CapacityProfile profile, CancellationToken ct)
    {
        var prefix = new BsonRegularExpression("^" + profile.Prefix);
        await Collection<OrganizationDocument>("ZumboOrganizations", "organizations").DeleteManyAsync(
            Builders<OrganizationDocument>.Filter.Regex("_id", prefix), ct);
        await Collection<UserDocument>("ZumboIdentity", "users").DeleteManyAsync(
            Builders<UserDocument>.Filter.Regex("_id", prefix), ct);
        await Collection<ProjectDocument>("ZumboProjects", "projects").DeleteManyAsync(
            Builders<ProjectDocument>.Filter.Regex("_id", prefix), ct);
        await Collection<BoardDocument>("ZumboBoards", "boards").DeleteManyAsync(
            Builders<BoardDocument>.Filter.Regex("_id", prefix), ct);
        await Collection<WorkItemDocument>("ZumboWorkItems", "workitems").DeleteManyAsync(
            Builders<WorkItemDocument>.Filter.Regex("_id", prefix), ct);
        await Collection<AuditLogDocument>("ZumboAudit", "auditlogs").DeleteManyAsync(
            Builders<AuditLogDocument>.Filter.Regex("_id", prefix), ct);

        var body = JsonContent.Create(new
        {
            query = new
            {
                prefix = new Dictionary<string, string> { ["projectId.keyword"] = profile.Prefix }
            }
        });
        using var response = await _search.PostAsync("zumbo-work-items/_delete_by_query?conflicts=proceed&refresh=true", body, ct);
        if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            response.EnsureSuccessStatusCode();
        }
    }

    private static List<UserDocument> BuildUsers(CapacityProfile profile, string passwordHash, DateTimeOffset now)
    {
        return Enumerable.Range(0, profile.UserCount).Select(index => new UserDocument
        {
            Id = CapacityIds.User(profile, index),
            Username = CapacityIds.Username(profile, index),
            Email = $"{CapacityIds.Username(profile, index)}@zumbo.local",
            OrganizationId = CapacityIds.Organization(profile, index % profile.OrganizationCount),
            PasswordHash = passwordHash,
            IsActive = true,
            SecurityStamp = $"{profile.Prefix}stamp-{index:D6}",
            Roles = ["User"],
            CreatedAt = now
        }).ToList();
    }

    private static List<OrganizationDocument> BuildOrganizations(CapacityProfile profile, DateTimeOffset now)
    {
        return Enumerable.Range(0, profile.OrganizationCount).Select(index => new OrganizationDocument
        {
            Id = CapacityIds.Organization(profile, index),
            TenantKey = CapacityIds.Organization(profile, index),
            Name = $"Capacity {profile.Name} organization {index + 1}",
            OwnerUserId = CapacityIds.User(profile, index),
            CreatedAt = now,
            UpdatedAt = now
        }).ToList();
    }

    private static List<ProjectDocument> BuildProjects(CapacityProfile profile, DateTimeOffset now)
    {
        return Enumerable.Range(0, profile.ProjectCount).Select(index =>
        {
            var organizationIndex = index % profile.OrganizationCount;
            return new ProjectDocument
            {
                Id = CapacityIds.Project(profile, index),
                OrganizationId = CapacityIds.Organization(profile, organizationIndex),
                Key = $"C{index:D4}",
                Name = $"Capacity {profile.Name} project {index + 1}",
                Visibility = "Internal",
                Members =
                [
                    new ProjectMemberDocument
                    {
                        UserId = CapacityIds.User(profile, organizationIndex),
                        Role = "ProjectOwner"
                    }
                ],
                CreatedAt = now,
                UpdatedAt = now
            };
        }).ToList();
    }

    private static List<BoardDocument> BuildBoards(CapacityProfile profile, DateTimeOffset now)
    {
        var columns = new[]
        {
            ("To Do", "Todo"),
            ("In Progress", "InProgress"),
            ("Code Review", "Review"),
            ("Test", "Test"),
            ("Done", "Done")
        };
        return Enumerable.Range(0, profile.ProjectCount).Select(projectIndex => new BoardDocument
        {
            Id = CapacityIds.Board(profile, projectIndex),
            ProjectId = CapacityIds.Project(profile, projectIndex),
            Name = "Capacity board",
            Type = "Kanban",
            Columns = columns.Select((column, columnIndex) => new BoardColumnDocument
            {
                Id = CapacityIds.Column(profile, projectIndex, columnIndex),
                Name = column.Item1,
                Category = column.Item2,
                Position = columnIndex + 1,
                WipLimit = null
            }).ToList(),
            CreatedAt = now,
            UpdatedAt = now
        }).ToList();
    }

    private async Task SeedWorkItemsAsync(CapacityProfile profile, DateTimeOffset now, CancellationToken ct)
    {
        var collection = Collection<WorkItemDocument>("ZumboWorkItems", "workitems");
        for (var start = 0; start < profile.WorkItemCount; start += DocumentBatchSize)
        {
            var count = Math.Min(DocumentBatchSize, profile.WorkItemCount - start);
            var batch = Enumerable.Range(start, count).Select(index => BuildWorkItem(profile, index, now)).ToList();
            await collection.InsertManyAsync(batch, new InsertManyOptions { IsOrdered = false }, ct);
            await BulkIndexAsync(batch, ct);
            Console.Error.WriteLine($"work-items: {start + count:N0}/{profile.WorkItemCount:N0}");
        }
    }

    private static WorkItemDocument BuildWorkItem(CapacityProfile profile, int index, DateTimeOffset now)
    {
        var statuses = new[] { "To Do", "In Progress", "Code Review", "Test", "Done", "Blocked" };
        var priorities = new[] { "Low", "Medium", "High", "Critical" };
        var projectIndex = index % profile.ProjectCount;
        var organizationIndex = projectIndex % profile.OrganizationCount;
        var statusIndex = index % statuses.Length;
        var columnIndex = statusIndex switch { 0 => 0, 1 or 5 => 1, 2 => 2, 3 => 3, _ => 4 };
        var createdAt = now.AddSeconds(-(profile.WorkItemCount - index));
        return new WorkItemDocument
        {
            Id = CapacityIds.WorkItem(profile, index),
            ProjectId = CapacityIds.Project(profile, projectIndex),
            BoardId = CapacityIds.Board(profile, projectIndex),
            ColumnId = CapacityIds.Column(profile, projectIndex, columnIndex),
            Title = $"Capacity common query item {index:D8}",
            Description = $"Bounded capacity dataset for {profile.Name}; project {projectIndex:D4}.",
            Type = index % 11 == 0 ? "Bug" : "Task",
            Priority = priorities[index % priorities.Length],
            Status = statuses[statusIndex],
            Rank = createdAt.UtcTicks,
            AssigneeUserId = CapacityIds.User(profile, organizationIndex),
            DueDate = now.AddDays((index % 60) - 15),
            SprintId = $"sprint-{index % 12:D2}",
            EstimatePoints = (index % 13) + 1,
            CompletedAt = statusIndex == 4 ? createdAt.AddDays(2) : null,
            Labels = ["capacity", index % 2 == 0 ? "backend" : "frontend"],
            StatusHistory =
            [
                new WorkItemStatusHistoryDocument
                {
                    FromStatus = null,
                    ToStatus = statuses[statusIndex],
                    ChangedByUserId = CapacityIds.User(profile, organizationIndex),
                    ChangedAt = createdAt
                }
            ],
            CreatedAt = createdAt,
            UpdatedAt = createdAt
        };
    }

    private async Task SeedAuditAsync(CapacityProfile profile, DateTimeOffset now, CancellationToken ct)
    {
        var collection = Collection<AuditLogDocument>("ZumboAudit", "auditlogs");
        for (var start = 0; start < profile.ActivityEventCount; start += AuditBatchSize)
        {
            var count = Math.Min(AuditBatchSize, profile.ActivityEventCount - start);
            var batch = Enumerable.Range(start, count).Select(index => new AuditLogDocument
            {
                Id = CapacityIds.Audit(profile, index),
                ActorUserId = CapacityIds.User(profile, index % profile.UserCount),
                Action = index % 3 == 0 ? "WorkItemUpdated" : "WorkItemViewed",
                EntityType = "WorkItem",
                EntityId = CapacityIds.WorkItem(profile, index % profile.WorkItemCount),
                OldValue = null,
                NewValue = $"capacity-event-{index}",
                IpAddress = "127.0.0.1",
                UserAgent = "Zumbo.Capacity/1.0",
                CorrelationId = $"capacity-{profile.Name}-{index:D9}",
                CreatedAt = now.AddMilliseconds(-index)
            }).ToList();
            await collection.InsertManyAsync(batch, new InsertManyOptions { IsOrdered = false }, ct);
            if ((start + count) % 100_000 == 0 || start + count == profile.ActivityEventCount)
            {
                Console.Error.WriteLine($"activity-events: {start + count:N0}/{profile.ActivityEventCount:N0}");
            }
        }
    }

    private async Task BulkIndexAsync(IReadOnlyCollection<WorkItemDocument> batch, CancellationToken ct)
    {
        var builder = new StringBuilder(batch.Count * 500);
        foreach (var item in batch)
        {
            builder.Append(JsonSerializer.Serialize(new { index = new { _index = "zumbo-work-items", _id = item.Id } })).Append('\n');
            builder.Append(JsonSerializer.Serialize(new WorkItemSearchRecord(
                item.Id,
                item.ProjectId,
                item.BoardId,
                item.Title,
                item.Description,
                item.Status,
                item.Priority,
                item.AssigneeUserId,
                item.Labels), JsonOptions)).Append('\n');
        }

        using var content = new StringContent(builder.ToString(), Encoding.UTF8, "application/x-ndjson");
        using var response = await _search.PostAsync("_bulk?refresh=false", content, ct);
        response.EnsureSuccessStatusCode();
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        if (payload.RootElement.TryGetProperty("errors", out var errors) && errors.GetBoolean())
        {
            throw new InvalidOperationException("OpenSearch bulk indexing returned item errors.");
        }
    }

    private async Task SetRefreshIntervalAsync(string value, CancellationToken ct)
    {
        using var response = await _search.PutAsJsonAsync(
            "zumbo-work-items/_settings",
            new { index = new { refresh_interval = value } },
            ct);
        response.EnsureSuccessStatusCode();
    }

    private IMongoCollection<T> Collection<T>(string database, string collection) =>
        _mongo.GetDatabase(database).GetCollection<T>(collection);

    private static async Task InsertAsync<T>(IMongoCollection<T> collection, IReadOnlyCollection<T> documents, CancellationToken ct)
    {
        foreach (var batch in documents.Chunk(DocumentBatchSize))
        {
            await collection.InsertManyAsync(batch, new InsertManyOptions { IsOrdered = false }, ct);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
