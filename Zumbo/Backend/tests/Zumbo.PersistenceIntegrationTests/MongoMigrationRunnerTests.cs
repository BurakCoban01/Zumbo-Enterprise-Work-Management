using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.WorkItems;

namespace Zumbo.PersistenceIntegrationTests;

public sealed class MongoMigrationRunnerTests : IAsyncLifetime
{
    private const string WorkItemsCollection = "workitems";
    private const string LedgerCollection = "__zumbo_migrations";
    private readonly string _connectionString;
    private readonly MongoClient _client;
    private readonly IMongoDatabase _database;

    public MongoMigrationRunnerTests()
    {
        var connectionString = Environment.GetEnvironmentVariable("ZUMBO_TEST_MONGO_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ZUMBO_TEST_MONGO_CONNECTION_STRING is required for real Mongo migration tests.");
        }

        _connectionString = connectionString;
        _client = new MongoClient(_connectionString);
        _database = _client.GetDatabase($"ZumboData003_{Guid.NewGuid():N}");
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() =>
        _client.DropDatabaseAsync(_database.DatabaseNamespace.DatabaseName);

    [Fact]
    public async Task Ledger_IsIdempotent_AndCompletedMigrationIsNotAppliedTwice()
    {
        await InsertWorkItemsAsync(
            WorkItem("missing-rank", Utc(2025, 1, 1)),
            WorkItem("zero-rank", Utc(2025, 1, 2), rank: 0));
        var runner = CreateRunner(new MongoMigrationOptions
        {
            RunDataMigrations = true,
            BatchSize = 1,
            MaxBatchesPerRun = 20
        });

        var first = Outcome(await runner.RunAsync(CancellationToken.None), MongoMigrationRunner.RankMigrationId);
        var afterFirstRun = await ReadWorkItemsAsync();
        var second = Outcome(await runner.RunAsync(CancellationToken.None), MongoMigrationRunner.RankMigrationId);

        Assert.Equal(MongoMigrationStates.Completed, first.Status);
        Assert.Equal(2, first.Changed);
        Assert.Equal(MongoMigrationStates.Skipped, second.Status);
        Assert.Equal(2, second.Changed);
        Assert.Equal(afterFirstRun, await ReadWorkItemsAsync());

        var ledger = _database.GetCollection<BsonDocument>(LedgerCollection);
        var entries = await ledger.Find(
                Builders<BsonDocument>.Filter.Eq("_id", MongoMigrationRunner.RankMigrationId))
            .ToListAsync();
        var entry = Assert.Single(entries);
        Assert.Equal(MongoMigrationStates.Completed, entry["State"].AsString);
    }

    [Fact]
    public async Task OrganizationVersionBackfill_IsBoundedAndIdempotent()
    {
        var organizations = _database.GetCollection<BsonDocument>("organizations");
        await organizations.InsertManyAsync(
        [
            new BsonDocument
            {
                ["_id"] = "legacy-organization",
                ["Name"] = "Legacy",
                ["TenantKey"] = "legacy-organization",
                ["OwnerUserId"] = "owner-1"
            },
            new BsonDocument
            {
                ["_id"] = "versioned-organization",
                ["Name"] = "Versioned",
                ["TenantKey"] = "versioned-organization",
                ["OwnerUserId"] = "owner-2",
                ["Status"] = "Suspended",
                ["Version"] = 5L
            }
        ]);
        var runner = CreateRunner(new MongoMigrationOptions
        {
            RunDataMigrations = true,
            BatchSize = 1,
            MaxBatchesPerRun = 20
        });

        var first = Outcome(
            await runner.RunAsync(CancellationToken.None),
            MongoMigrationRunner.OrganizationVersionMigrationId);
        var second = Outcome(
            await runner.RunAsync(CancellationToken.None),
            MongoMigrationRunner.OrganizationVersionMigrationId);
        var legacy = await organizations.Find(
            Builders<BsonDocument>.Filter.Eq("_id", "legacy-organization")).SingleAsync();
        var versioned = await organizations.Find(
            Builders<BsonDocument>.Filter.Eq("_id", "versioned-organization")).SingleAsync();

        Assert.Equal(MongoMigrationStates.Completed, first.Status);
        Assert.Equal(1, first.Changed);
        Assert.Equal(MongoMigrationStates.Skipped, second.Status);
        Assert.Equal(1L, legacy["Version"].AsInt64);
        Assert.Equal("Active", legacy["Status"].AsString);
        Assert.Equal(5L, versioned["Version"].AsInt64);
        Assert.Equal("Suspended", versioned["Status"].AsString);
    }

    [Fact]
    public async Task UserVersionBackfill_RepairsOnlyLegacyVersionsAndIsIdempotent()
    {
        var users = _database.GetCollection<BsonDocument>("users");
        await users.InsertManyAsync(
        [
            new BsonDocument { ["_id"] = "missing-version", ["Username"] = "missing" },
            new BsonDocument { ["_id"] = "zero-version", ["Username"] = "zero", ["Version"] = 0L },
            new BsonDocument { ["_id"] = "current-version", ["Username"] = "current", ["Version"] = 8L }
        ]);
        var runner = CreateRunner(new MongoMigrationOptions
        {
            RunDataMigrations = true,
            BatchSize = 1,
            MaxBatchesPerRun = 20
        });

        var first = Outcome(
            await runner.RunAsync(CancellationToken.None),
            MongoMigrationRunner.UserVersionMigrationId);
        var second = Outcome(
            await runner.RunAsync(CancellationToken.None),
            MongoMigrationRunner.UserVersionMigrationId);
        var documents = await users.Find(FilterDefinition<BsonDocument>.Empty)
            .Sort(new BsonDocument("_id", 1))
            .ToListAsync();

        Assert.Equal(MongoMigrationStates.Completed, first.Status);
        Assert.Equal(2, first.Changed);
        Assert.Equal(MongoMigrationStates.Skipped, second.Status);
        Assert.Equal(8L, documents.Single(x => x["_id"] == "current-version")["Version"].AsInt64);
        Assert.All(documents.Where(x => x["_id"] != "current-version"),
            document => Assert.Equal(1L, document["Version"].AsInt64));
    }

    [Fact]
    public async Task LegacyMigrationMarkerCleanup_RemovesOnlyInfrastructureMarkers()
    {
        var projects = _database.GetCollection<BsonDocument>("projects");
        await projects.InsertOneAsync(new BsonDocument
        {
            ["_id"] = "marked-project",
            ["Name"] = "Preserved project",
            ["Version"] = 4L,
            ["ProjectLifecycleMigratedBy"] = MongoMigrationRunner.ProjectLifecycleMigrationId
        });
        var runner = CreateRunner(new MongoMigrationOptions { BatchSize = 10, MaxBatchesPerRun = 20 });

        var first = Outcome(
            await runner.RunAsync(CancellationToken.None),
            MongoMigrationRunner.LegacyMigrationMarkerCleanupId);
        var second = Outcome(
            await runner.RunAsync(CancellationToken.None),
            MongoMigrationRunner.LegacyMigrationMarkerCleanupId);
        var project = await projects.Find(
            Builders<BsonDocument>.Filter.Eq("_id", "marked-project")).SingleAsync();

        Assert.Equal(MongoMigrationStates.Completed, first.Status);
        Assert.Equal(1, first.Changed);
        Assert.Equal(MongoMigrationStates.Skipped, second.Status);
        Assert.False(project.Contains("ProjectLifecycleMigratedBy"));
        Assert.Equal("Preserved project", project["Name"].AsString);
        Assert.Equal(4L, project["Version"].AsInt64);
    }

    [Fact]
    public async Task TeamInviteTokenBackfill_ExpiresOnlyLegacyHashlessInvites()
    {
        var teams = _database.GetCollection<BsonDocument>("teams");
        await teams.InsertOneAsync(new BsonDocument
        {
            ["_id"] = "legacy-team",
            ["OrganizationId"] = "org-legacy",
            ["Name"] = "Legacy Team",
            ["Version"] = 4L,
            ["Members"] = new BsonArray
            {
                new BsonDocument
                {
                    ["Id"] = "legacy-invite",
                    ["Email"] = "legacy@zumbo.local",
                    ["Role"] = "Member",
                    ["Status"] = "Invited",
                    ["InvitationExpiresAt"] = Utc(2026, 7, 27)
                },
                new BsonDocument
                {
                    ["Id"] = "secure-invite",
                    ["Email"] = "secure@zumbo.local",
                    ["Role"] = "Member",
                    ["Status"] = "Invited",
                    ["InvitationTokenHash"] = new string('a', 64),
                    ["InvitationExpiresAt"] = Utc(2026, 7, 27)
                }
            }
        });
        var runner = CreateRunner(new MongoMigrationOptions
        {
            RunDataMigrations = true,
            BatchSize = 1,
            MaxBatchesPerRun = 20
        });

        var first = Outcome(
            await runner.RunAsync(CancellationToken.None),
            MongoMigrationRunner.TeamInviteTokenMigrationId);
        var second = Outcome(
            await runner.RunAsync(CancellationToken.None),
            MongoMigrationRunner.TeamInviteTokenMigrationId);
        var migrated = await teams.Find(
            Builders<BsonDocument>.Filter.Eq("_id", "legacy-team")).SingleAsync();
        var legacy = migrated["Members"].AsBsonArray[0].AsBsonDocument;
        var secure = migrated["Members"].AsBsonArray[1].AsBsonDocument;

        Assert.Equal(MongoMigrationStates.Completed, first.Status);
        Assert.Equal(1, first.Changed);
        Assert.Equal(MongoMigrationStates.Skipped, second.Status);
        Assert.Equal(5L, migrated["Version"].AsInt64);
        Assert.Equal("Expired", legacy["Status"].AsString);
        Assert.True(legacy["InvitationTokenHash"].IsBsonNull);
        Assert.Equal("Invited", secure["Status"].AsString);
        Assert.Equal(new string('a', 64), secure["InvitationTokenHash"].AsString);
    }

    [Fact]
    public async Task ProjectLifecycleBackfill_IsBoundedAndIdempotent()
    {
        var projects = _database.GetCollection<BsonDocument>("projects");
        await projects.InsertManyAsync(
        [
            new BsonDocument
            {
                ["_id"] = "legacy-project",
                ["OrganizationId"] = "org-legacy",
                ["Key"] = "LEGACY",
                ["Name"] = "Legacy Project",
                ["Members"] = new BsonArray(),
                ["TeamIds"] = new BsonArray()
            },
            new BsonDocument
            {
                ["_id"] = "current-project",
                ["OrganizationId"] = "org-current",
                ["Key"] = "CURRENT",
                ["Name"] = "Current Project",
                ["Visibility"] = "Private",
                ["Archived"] = false,
                ["Members"] = new BsonArray(),
                ["TeamIds"] = new BsonArray(),
                ["Templates"] = new BsonArray(),
                ["Components"] = new BsonArray(),
                ["Versions"] = new BsonArray(),
                ["Releases"] = new BsonArray(),
                ["Milestones"] = new BsonArray(),
                ["ArchivedAt"] = BsonNull.Value,
                ["RetainUntil"] = BsonNull.Value,
                ["Version"] = 7L
            }
        ]);
        var runner = CreateRunner(new MongoMigrationOptions
        {
            RunDataMigrations = true,
            BatchSize = 1,
            MaxBatchesPerRun = 20
        });

        var first = Outcome(
            await runner.RunAsync(CancellationToken.None),
            MongoMigrationRunner.ProjectLifecycleMigrationId);
        var second = Outcome(
            await runner.RunAsync(CancellationToken.None),
            MongoMigrationRunner.ProjectLifecycleMigrationId);
        var legacy = await projects.Find(
            Builders<BsonDocument>.Filter.Eq("_id", "legacy-project")).SingleAsync();
        var current = await projects.Find(
            Builders<BsonDocument>.Filter.Eq("_id", "current-project")).SingleAsync();

        Assert.Equal(MongoMigrationStates.Completed, first.Status);
        Assert.Equal(1, first.Changed);
        Assert.Equal(MongoMigrationStates.Skipped, second.Status);
        Assert.Equal(1L, legacy["Version"].AsInt64);
        Assert.Equal("Internal", legacy["Visibility"].AsString);
        Assert.Empty(legacy["Templates"].AsBsonArray);
        Assert.Empty(legacy["Components"].AsBsonArray);
        Assert.Empty(legacy["Releases"].AsBsonArray);
        Assert.True(legacy["RetainUntil"].IsBsonNull);
        Assert.Equal(7L, current["Version"].AsInt64);
        Assert.Equal("Private", current["Visibility"].AsString);
    }

    [Fact]
    public async Task WorkItemActivityBackfill_IsBoundedResumableAndIdempotent()
    {
        await _database.GetCollection<BsonDocument>("projects").InsertOneAsync(new BsonDocument
        {
            ["_id"] = "project-data007",
            ["OrganizationId"] = "org-data007"
        });
        for (var index = 1; index <= 3; index++)
        {
            var at = Utc(2026, 7, index);
            await InsertWorkItemsAsync(new BsonDocument
            {
                ["_id"] = $"data007-item-{index}",
                ["ProjectId"] = "project-data007",
                ["BoardId"] = "board-data007",
                ["Version"] = 0L,
                ["Rank"] = index,
                ["CreatedAt"] = at,
                ["Comments"] = new BsonArray
                {
                    new BsonDocument
                    {
                        ["Id"] = $"data007-comment-{index}",
                        ["Body"] = $"comment-{index}",
                        ["AuthorUserId"] = "user-a",
                        ["Mentions"] = new BsonArray { "user-b" },
                        ["CreatedAt"] = at,
                        ["EditedAt"] = BsonNull.Value,
                        ["History"] = new BsonArray
                        {
                            new BsonDocument
                            {
                                ["Body"] = $"old-{index}",
                                ["EditedByUserId"] = "user-a",
                                ["EditedAt"] = at.AddMinutes(1)
                            }
                        }
                    }
                },
                ["Attachments"] = new BsonArray
                {
                    new BsonDocument { ["Id"] = $"data007-attachment-{index}", ["FileName"] = "a.txt", ["ContentType"] = "text/plain", ["SizeBytes"] = 1L, ["StoragePath"] = "a", ["ChecksumSha256"] = "a", ["CreatedAt"] = at }
                },
                ["WorkLogs"] = new BsonArray
                {
                    new BsonDocument { ["Id"] = $"data007-log-{index}", ["UserId"] = "user-a", ["Hours"] = 1m, ["Note"] = BsonNull.Value, ["CreatedAt"] = at }
                },
                ["Approvals"] = new BsonArray
                {
                    new BsonDocument { ["Id"] = $"data007-approval-{index}", ["FromStatus"] = "To Do", ["ToStatus"] = "Done", ["RequestedByUserId"] = "user-a", ["RequestedAt"] = at, ["ExpiresAt"] = at.AddDays(1), ["Status"] = "Pending" }
                },
                ["StatusHistory"] = new BsonArray
                {
                    new BsonDocument { ["FromStatus"] = BsonNull.Value, ["ToStatus"] = "To Do", ["ChangedByUserId"] = "user-a", ["ChangedAt"] = at }
                }
            });
        }

        var paused = Outcome(await CreateRunner(new MongoMigrationOptions
        {
            RunDataMigrations = true,
            BatchSize = 1,
            MaxBatchesPerRun = 1
        }).RunAsync(CancellationToken.None), MongoMigrationRunner.WorkItemActivityMigrationId);
        Assert.Equal(MongoMigrationStates.Paused, paused.Status);
        Assert.Equal(1, paused.Changed);

        var completed = Outcome(await CreateRunner(new MongoMigrationOptions
        {
            RunDataMigrations = true,
            BatchSize = 1,
            MaxBatchesPerRun = 20
        }).RunAsync(CancellationToken.None), MongoMigrationRunner.WorkItemActivityMigrationId);
        Assert.Equal(MongoMigrationStates.Completed, completed.Status);
        Assert.Equal(3, completed.Changed);

        var migrated = await ReadWorkItemsAsync();
        Assert.All(migrated.Where(x => x["_id"].AsString.StartsWith("data007-")), item =>
        {
            Assert.Equal(1, item["ActivityStorageVersion"].AsInt32);
            Assert.Empty(item["Comments"].AsBsonArray);
            Assert.Empty(item["Attachments"].AsBsonArray);
            Assert.Empty(item["WorkLogs"].AsBsonArray);
            Assert.Empty(item["Approvals"].AsBsonArray);
            Assert.Empty(item["StatusHistory"].AsBsonArray);
        });
        Assert.Equal(3, await _database.GetCollection<BsonDocument>("workitemcommentactivitys").CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));
        Assert.Equal(3, await _database.GetCollection<BsonDocument>("workitemcommentrevisionactivitys").CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));
        Assert.Equal(3, await _database.GetCollection<BsonDocument>("workitemattachmentactivitys").CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));
        Assert.Equal(3, await _database.GetCollection<BsonDocument>("workitemworklogactivitys").CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));
        Assert.Equal(3, await _database.GetCollection<BsonDocument>("workitemapprovalactivitys").CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));
        Assert.Equal(3, await _database.GetCollection<BsonDocument>("workitemtimelineactivitys").CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));

        var rerun = Outcome(await CreateRunner(new MongoMigrationOptions
        {
            RunDataMigrations = true,
            BatchSize = 10,
            MaxBatchesPerRun = 20
        }).RunAsync(CancellationToken.None), MongoMigrationRunner.WorkItemActivityMigrationId);
        Assert.Equal(MongoMigrationStates.Skipped, rerun.Status);
        Assert.Equal(3, rerun.Changed);
    }

    [Fact]
    public async Task DryRun_WritesNothing_IncludingLedgerDocumentsAndIndexes()
    {
        await InsertWorkItemsAsync(
            WorkItem("missing-rank", Utc(2025, 2, 1)),
            WorkItem("zero-rank", Utc(2025, 2, 2), rank: 0));
        var before = await SnapshotDatabaseAsync();
        var runner = CreateRunner(new MongoMigrationOptions
        {
            DryRun = true,
            RunDataMigrations = true,
            BatchSize = 1,
            MaxBatchesPerRun = 20
        });

        var result = Outcome(await runner.RunAsync(CancellationToken.None), MongoMigrationRunner.RankMigrationId);

        Assert.Equal(MongoMigrationStates.DryRun, result.Status);
        Assert.Equal(0, result.Changed);
        Assert.Equal(2, result.Examined);
        Assert.Equal(before, await SnapshotDatabaseAsync());
    }

    [Fact]
    public async Task BatchCheckpoint_CanPauseResumeAndRollbackToExactOriginalBson()
    {
        await InsertWorkItemsAsync(
            WorkItem("item-1", Utc(2025, 3, 1)),
            WorkItem("item-2", Utc(2025, 3, 2), rank: 0),
            WorkItem("item-3", Utc(2025, 3, 3)),
            WorkItem("item-4", Utc(2025, 3, 4), rank: 0),
            WorkItem("item-5", Utc(2025, 3, 5)));
        var original = await ReadWorkItemsAsync();
        var pausedRunner = CreateRunner(new MongoMigrationOptions
        {
            RunDataMigrations = true,
            BatchSize = 2,
            MaxBatchesPerRun = 1
        });

        var paused = Outcome(
            await pausedRunner.RunAsync(CancellationToken.None),
            MongoMigrationRunner.RankMigrationId);

        Assert.Equal(MongoMigrationStates.Paused, paused.Status);
        Assert.Equal(2, paused.Changed);
        Assert.Equal(2, await CountPositiveRanksAsync());
        var pausedLedger = await ReadLedgerEntryAsync(MongoMigrationRunner.RankMigrationId);
        Assert.Equal(MongoMigrationStates.Paused, pausedLedger["State"].AsString);
        Assert.False(pausedLedger["Checkpoint"].IsBsonNull);

        var resumedRunner = CreateRunner(new MongoMigrationOptions
        {
            RunDataMigrations = true,
            BatchSize = 2,
            MaxBatchesPerRun = 20
        });
        var resumed = Outcome(
            await resumedRunner.RunAsync(CancellationToken.None),
            MongoMigrationRunner.RankMigrationId);

        Assert.Equal(MongoMigrationStates.Completed, resumed.Status);
        Assert.Equal(5, await CountPositiveRanksAsync());
        Assert.Equal(MongoMigrationStates.Completed, (await ReadLedgerEntryAsync(
            MongoMigrationRunner.RankMigrationId))["State"].AsString);

        var rollbackRunner = CreateRunner(new MongoMigrationOptions { BatchSize = 2, MaxBatchesPerRun = 20 });
        var rolledBack = await rollbackRunner.RollbackAsync(
            MongoMigrationRunner.RankMigrationId,
            CancellationToken.None);

        Assert.Equal(MongoMigrationStates.RolledBack, rolledBack.Status);
        Assert.Equal(original, await ReadWorkItemsAsync());
        Assert.Equal(MongoMigrationStates.RolledBack, (await ReadLedgerEntryAsync(
            MongoMigrationRunner.RankMigrationId))["State"].AsString);
    }

    [Fact]
    public async Task RankBackfill_HandlesRepresentativeBsonAndRerunWithoutCorruptingData()
    {
        await InsertWorkItemsAsync(
            WorkItem("missing-rank", Utc(2025, 4, 1)),
            WorkItem("zero-rank", Utc(2025, 4, 2), rank: 0),
            WorkItem("valid-rank", Utc(2025, 4, 3), rank: 9_999_999),
            WorkItem("invalid-rank", Utc(2025, 4, 4), rankValue: "legacy"),
            new BsonDocument
            {
                ["_id"] = "offset-array",
                ["CreatedAt"] = new BsonArray { Utc(2025, 4, 5).Ticks, 0 }
            },
            new BsonDocument { ["_id"] = "raw-ticks", ["CreatedAt"] = Utc(2025, 4, 6).Ticks },
            new BsonDocument { ["_id"] = "invalid-date", ["CreatedAt"] = "not-a-date" },
            new BsonDocument { ["_id"] = "invalid-array", ["CreatedAt"] = new BsonArray { "bad", 0 } },
            new BsonDocument { ["_id"] = "missing-date" },
            new BsonDocument { ["_id"] = "partial", ["CreatedAt"] = Utc(2025, 4, 7) });
        var runner = CreateRunner(new MongoMigrationOptions
        {
            RunDataMigrations = true,
            BatchSize = 2,
            MaxBatchesPerRun = 20
        });

        var first = Outcome(await runner.RunAsync(CancellationToken.None), MongoMigrationRunner.RankMigrationId);
        var afterFirstRun = await ReadWorkItemsAsync();
        var byId = afterFirstRun.ToDictionary(x => x["_id"].AsString, StringComparer.Ordinal);

        Assert.Equal(MongoMigrationStates.Completed, first.Status);
        Assert.Equal(5, first.Changed);
        Assert.Equal(3, first.Skipped);
        Assert.True(byId["missing-rank"]["Rank"].ToInt64() > 0);
        Assert.True(byId["zero-rank"]["Rank"].ToInt64() > byId["missing-rank"]["Rank"].ToInt64());
        Assert.True(byId["partial"]["Rank"].ToInt64() > byId["zero-rank"]["Rank"].ToInt64());
        Assert.Equal(9_999_999, byId["valid-rank"]["Rank"].ToInt64());
        Assert.Equal("legacy", byId["invalid-rank"]["Rank"].AsString);
        Assert.Equal(Utc(2025, 4, 5).Ticks, byId["offset-array"]["Rank"].ToInt64());
        Assert.Equal(Utc(2025, 4, 6).Ticks, byId["raw-ticks"]["Rank"].ToInt64());
        Assert.False(byId["invalid-date"].Contains("Rank"));
        Assert.False(byId["invalid-array"].Contains("Rank"));
        Assert.False(byId["missing-date"].Contains("Rank"));

        var rerun = Outcome(await runner.RunAsync(CancellationToken.None), MongoMigrationRunner.RankMigrationId);

        Assert.Equal(MongoMigrationStates.Skipped, rerun.Status);
        Assert.Equal(5, rerun.Changed);
        Assert.Equal(afterFirstRun, await ReadWorkItemsAsync());
    }

    [Fact]
    public async Task RequiredIndexCatalog_IsExact_AndMigrationCreatesTheDeclaredInventoryIdempotently()
    {
        var expected = RequiredIndexes();
        var catalog = MongoRequiredIndexes.All;

        Assert.Equal(expected.Count, catalog.Count);
        Assert.Equal(catalog.Count, catalog.Select(x => (x.Module, x.Collection, x.Name)).Distinct().Count());
        foreach (var required in expected)
        {
            var declared = Assert.Single(catalog, x =>
                x.Module == required.Module
                && x.Collection == required.CollectionName
                && x.Name == required.Name);
            Assert.Equal(required.Keys, declared.Keys);
            Assert.Equal(required.Unique, declared.Unique);
            Assert.Equal(required.ExpireAfter, declared.ExpireAfter);
            Assert.Equal(required.PartialFilter, declared.PartialFilter);
            Assert.Equal(required.CaseInsensitive, declared.CaseInsensitive);
        }

        var runner = CreateRunner(new MongoMigrationOptions { BatchSize = 10, MaxBatchesPerRun = 20 });
        var first = Outcome(await runner.RunAsync(CancellationToken.None), MongoMigrationRunner.IndexMigrationId);
        var second = Outcome(await runner.RunAsync(CancellationToken.None), MongoMigrationRunner.IndexMigrationId);

        Assert.Equal(MongoMigrationStates.Completed, first.Status);
        Assert.Equal(MongoMigrationStates.Skipped, second.Status);

        foreach (var required in expected)
        {
            var replacement = MongoNotificationDeliveryIndexes.All.SingleOrDefault(x =>
                    x.Module == required.Module
                    && x.Collection == required.CollectionName
                    && x.Name == required.Name);
            var effectiveKeys = replacement?.Keys ?? required.Keys;
            var effectiveUnique = replacement?.Unique ?? required.Unique;
            var effectiveExpireAfter = replacement?.ExpireAfter ?? required.ExpireAfter;
            var effectivePartialFilter = replacement?.PartialFilter ?? required.PartialFilter;
            var effectiveCaseInsensitive = replacement?.CaseInsensitive ?? required.CaseInsensitive;
            var collection = _database.GetCollection<BsonDocument>(required.CollectionName);
            using var cursor = await collection.Indexes.ListAsync();
            var indexes = await cursor.ToListAsync();
            var actual = Assert.Single(indexes, x => x["name"].AsString == required.Name);
            if (effectiveKeys.Values.Any(x => x.IsString && x.AsString == "text"))
            {
                Assert.Equal(new BsonDocument { ["_fts"] = "text", ["_ftsx"] = 1 }, actual["key"].AsBsonDocument);
                var weights = actual["weights"].AsBsonDocument;
                foreach (var textField in effectiveKeys.Names)
                {
                    Assert.True(weights.Contains(textField), $"Text index does not cover '{textField}'.");
                }
            }
            else
            {
                Assert.Equal(effectiveKeys, actual["key"].AsBsonDocument);
            }
            Assert.Equal(effectiveUnique, actual.GetValue("unique", false).ToBoolean());

            if (effectiveExpireAfter is not null)
            {
                Assert.Equal(
                    Convert.ToInt64(effectiveExpireAfter.Value.TotalSeconds),
                    actual["expireAfterSeconds"].ToInt64());
            }
            else
            {
                Assert.False(actual.Contains("expireAfterSeconds"));
            }

            if (effectivePartialFilter is not null)
            {
                Assert.Equal(effectivePartialFilter, actual["partialFilterExpression"].AsBsonDocument);
            }
            else
            {
                Assert.False(actual.Contains("partialFilterExpression"));
            }

            if (effectiveCaseInsensitive)
            {
                Assert.Equal("en", actual["collation"]["locale"].AsString);
                Assert.Equal(2, actual["collation"]["strength"].ToInt32());
            }
        }

        var teams = _database.GetCollection<BsonDocument>("teams");
        await teams.InsertOneAsync(new BsonDocument
        {
            ["_id"] = "team-a",
            ["OrganizationId"] = "org-a",
            ["Name"] = "Platform",
            ["Archived"] = true
        });
        var teamConflict = await Assert.ThrowsAsync<MongoWriteException>(() => teams.InsertOneAsync(new BsonDocument
        {
            ["_id"] = "team-b",
            ["OrganizationId"] = "org-a",
            ["Name"] = "platform",
            ["Archived"] = false
        }));
        Assert.Equal(ServerErrorCategory.DuplicateKey, teamConflict.WriteError.Category);

        var boards = _database.GetCollection<BsonDocument>("boards");
        await boards.InsertOneAsync(new BsonDocument
        {
            ["_id"] = "board-a",
            ["ProjectId"] = "project-a",
            ["Name"] = "Delivery",
            ["Archived"] = false
        });
        var boardConflict = await Assert.ThrowsAsync<MongoWriteException>(() => boards.InsertOneAsync(new BsonDocument
        {
            ["_id"] = "board-b",
            ["ProjectId"] = "project-a",
            ["Name"] = "delivery",
            ["Archived"] = false
        }));
        Assert.Equal(ServerErrorCategory.DuplicateKey, boardConflict.WriteError.Category);
        await boards.InsertOneAsync(new BsonDocument
        {
            ["_id"] = "board-c",
            ["ProjectId"] = "project-a",
            ["Name"] = "delivery",
            ["Archived"] = true
        });

        var offsetDate = new BsonArray { Utc(2025, 7, 1).Ticks, 0 };
        await _database.GetCollection<BsonDocument>("workitems").InsertOneAsync(new BsonDocument
        {
            ["_id"] = "legacy-offset-dates",
            ["ProjectId"] = "project-a",
            ["BoardId"] = "board-a",
            ["ColumnId"] = "column-a",
            ["Archived"] = false,
            ["Rank"] = 1000,
            ["CreatedAt"] = offsetDate,
            ["CompletedAt"] = offsetDate,
            ["DueDate"] = offsetDate,
            ["DueReminderSentAt"] = offsetDate
        });
        await _database.GetCollection<BsonDocument>("notifications").InsertOneAsync(new BsonDocument
        {
            ["_id"] = "legacy-notification-dates",
            ["UserId"] = "user-a",
            ["Read"] = false,
            ["EmailStatus"] = "Pending",
            ["EmailNextAttemptAt"] = offsetDate,
            ["CreatedAt"] = offsetDate
        });
    }

    [Fact]
    public async Task DurableMessagingIndexes_AreExactAndCreatedIdempotently()
    {
        var catalog = MongoDurableMessagingIndexes.All;
        Assert.Equal(6, catalog.Count);
        Assert.Equal(catalog.Count, catalog.Select(x => (x.Module, x.Collection, x.Name)).Distinct().Count());

        var runner = CreateRunner(new MongoMigrationOptions { BatchSize = 10, MaxBatchesPerRun = 20 });
        var first = Outcome(
            await runner.RunAsync(CancellationToken.None),
            MongoMigrationRunner.DurableMessagingIndexMigrationId);
        var second = Outcome(
            await runner.RunAsync(CancellationToken.None),
            MongoMigrationRunner.DurableMessagingIndexMigrationId);

        Assert.Equal(MongoMigrationStates.Completed, first.Status);
        Assert.Equal(catalog.Count, first.Examined);
        Assert.Equal(MongoMigrationStates.Skipped, second.Status);

        foreach (var specification in catalog)
        {
            var collection = _database.GetCollection<BsonDocument>(specification.Collection);
            using var cursor = await collection.Indexes.ListAsync();
            var indexes = await cursor.ToListAsync();
            var actual = Assert.Single(indexes, x => x["name"].AsString == specification.Name);

            Assert.Equal(specification.Keys, actual["key"].AsBsonDocument);
            Assert.Equal(specification.Unique, actual.GetValue("unique", false).ToBoolean());
            if (specification.PartialFilter is null)
            {
                Assert.False(actual.Contains("partialFilterExpression"));
            }
            else
            {
                Assert.Equal(specification.PartialFilter, actual["partialFilterExpression"].AsBsonDocument);
            }
        }
    }

    [Fact]
    public async Task WorkItemActivityIndexes_AreExactAndCreatedIdempotently()
    {
        var catalog = MongoWorkItemActivityIndexes.All;
        Assert.Equal(6, catalog.Count);
        Assert.Equal(catalog.Count, catalog.Select(x => (x.Module, x.Collection, x.Name)).Distinct().Count());

        var runner = CreateRunner(new MongoMigrationOptions { BatchSize = 10, MaxBatchesPerRun = 20 });
        var first = Outcome(
            await runner.RunAsync(CancellationToken.None),
            MongoMigrationRunner.WorkItemActivityIndexMigrationId);
        var second = Outcome(
            await runner.RunAsync(CancellationToken.None),
            MongoMigrationRunner.WorkItemActivityIndexMigrationId);

        Assert.Equal(MongoMigrationStates.Completed, first.Status);
        Assert.Equal(catalog.Count, first.Examined);
        Assert.Equal(MongoMigrationStates.Skipped, second.Status);

        foreach (var specification in catalog)
        {
            var collection = _database.GetCollection<BsonDocument>(specification.Collection);
            using var cursor = await collection.Indexes.ListAsync();
            var indexes = await cursor.ToListAsync();
            var actual = Assert.Single(indexes, x => x["name"].AsString == specification.Name);
            Assert.Equal(specification.Keys, actual["key"].AsBsonDocument);
            Assert.False(actual.GetValue("unique", false).ToBoolean());
        }
    }

    [Fact]
    public async Task WorkItemCollaborationIndexes_AreExactAndCreatedIdempotently()
    {
        var catalog = MongoWorkItemCollaborationIndexes.All;
        Assert.Equal(8, catalog.Count);
        Assert.Equal(catalog.Count, catalog.Select(x => (x.Module, x.Collection, x.Name)).Distinct().Count());

        var runner = CreateRunner(new MongoMigrationOptions { BatchSize = 10, MaxBatchesPerRun = 20 });
        var first = Outcome(
            await runner.RunAsync(CancellationToken.None),
            MongoMigrationRunner.WorkItemCollaborationIndexMigrationId);
        var second = Outcome(
            await runner.RunAsync(CancellationToken.None),
            MongoMigrationRunner.WorkItemCollaborationIndexMigrationId);

        Assert.Equal(MongoMigrationStates.Completed, first.Status);
        Assert.Equal(catalog.Count, first.Examined);
        Assert.Equal(MongoMigrationStates.Skipped, second.Status);
        foreach (var specification in catalog)
        {
            var collection = _database.GetCollection<BsonDocument>(specification.Collection);
            using var cursor = await collection.Indexes.ListAsync();
            var indexes = await cursor.ToListAsync();
            var actual = Assert.Single(indexes, index => index["name"].AsString == specification.Name);
            Assert.Equal(specification.Keys, actual["key"].AsBsonDocument);
            Assert.Equal(specification.Unique, actual.GetValue("unique", false).ToBoolean());
            if (specification.PartialFilter is not null)
            {
                Assert.Equal(specification.PartialFilter, actual["partialFilterExpression"].AsBsonDocument);
            }
            if (specification.CaseInsensitive)
            {
                Assert.Equal(2, actual["collation"]["strength"].ToInt32());
            }
        }
    }

    [Fact]
    public async Task WorkItemBulkJobIndexes_AreExactAndCreatedIdempotently()
    {
        var catalog = MongoWorkItemBulkJobIndexes.All;
        Assert.Equal(5, catalog.Count);
        Assert.Equal(catalog.Count, catalog.Select(x => (x.Module, x.Collection, x.Name)).Distinct().Count());

        var runner = CreateRunner(new MongoMigrationOptions { BatchSize = 10, MaxBatchesPerRun = 20 });
        var first = Outcome(
            await runner.RunAsync(CancellationToken.None),
            MongoMigrationRunner.WorkItemBulkJobIndexMigrationId);
        var second = Outcome(
            await runner.RunAsync(CancellationToken.None),
            MongoMigrationRunner.WorkItemBulkJobIndexMigrationId);

        Assert.Equal(MongoMigrationStates.Completed, first.Status);
        Assert.Equal(catalog.Count, first.Examined);
        Assert.Equal(MongoMigrationStates.Skipped, second.Status);
        foreach (var specification in catalog)
        {
            var collection = _database.GetCollection<BsonDocument>(specification.Collection);
            using var cursor = await collection.Indexes.ListAsync();
            var indexes = await cursor.ToListAsync();
            var actual = Assert.Single(indexes, index => index["name"].AsString == specification.Name);
            Assert.Equal(specification.Keys, actual["key"].AsBsonDocument);
            Assert.Equal(specification.Unique, actual.GetValue("unique", false).ToBoolean());
        }
    }

    [Fact]
    public async Task AuditTenantIndexes_AreExactAndCreatedIdempotently()
    {
        var catalog = MongoAuditTenantIndexes.All;
        Assert.Equal(4, catalog.Count);
        var runner = CreateRunner(new MongoMigrationOptions { BatchSize = 10, MaxBatchesPerRun = 20 });
        var first = Outcome(await runner.RunAsync(), MongoMigrationRunner.AuditTenantIndexMigrationId);
        var second = Outcome(await runner.RunAsync(), MongoMigrationRunner.AuditTenantIndexMigrationId);
        Assert.Equal(MongoMigrationStates.Completed, first.Status);
        Assert.Equal(catalog.Count, first.Examined);
        Assert.Equal(MongoMigrationStates.Skipped, second.Status);
        foreach (var specification in catalog)
        {
            var collection = _database.GetCollection<BsonDocument>(specification.Collection);
            using var cursor = await collection.Indexes.ListAsync();
            var actual = Assert.Single(await cursor.ToListAsync(), index => index["name"] == specification.Name);
            Assert.Equal(specification.Keys, actual["key"].AsBsonDocument);
        }
    }

    [Fact]
    public async Task WorkItemReportingIndexes_AreIdempotentAndKeepLargeProjectCursorIndexed()
    {
        var runner = CreateRunner(new MongoMigrationOptions { BatchSize = 10, MaxBatchesPerRun = 20 });
        var firstReport = await runner.RunAsync();
        var secondReport = await runner.RunAsync();
        var first = Outcome(firstReport, MongoMigrationRunner.WorkItemReportingIndexMigrationId);
        var second = Outcome(secondReport, MongoMigrationRunner.WorkItemReportingIndexMigrationId);
        var firstActivity = Outcome(firstReport, MongoMigrationRunner.WorkItemReportActivityIndexMigrationId);
        var secondActivity = Outcome(secondReport, MongoMigrationRunner.WorkItemReportActivityIndexMigrationId);

        Assert.Equal(MongoMigrationStates.Completed, first.Status);
        Assert.Equal(MongoWorkItemReportingIndexes.All.Count, first.Examined);
        Assert.Equal(MongoMigrationStates.Skipped, second.Status);
        Assert.Equal(MongoMigrationStates.Completed, firstActivity.Status);
        Assert.Equal(MongoWorkItemReportActivityIndexes.All.Count, firstActivity.Examined);
        Assert.Equal(MongoMigrationStates.Skipped, secondActivity.Status);

        var workItems = _database.GetCollection<BsonDocument>(WorkItemsCollection);
        await workItems.InsertManyAsync(Enumerable.Range(0, 5_000).Select(index => new BsonDocument
        {
            ["_id"] = $"report-plan-{index:D5}",
            ["ProjectId"] = index < 1_000 ? "report-target" : "report-other",
            ["Archived"] = false,
            ["TeamId"] = index % 2 == 0 ? "team-a" : "team-b",
            ["CreatedAt"] = Utc(2026, 7, 1).AddMinutes(index),
            ["Version"] = 1L
        }));

        var explainCommand = new BsonDocument
        {
            ["explain"] = new BsonDocument
            {
                ["find"] = WorkItemsCollection,
                ["filter"] = new BsonDocument
                {
                    ["ProjectId"] = "report-target",
                    ["Archived"] = false,
                    ["_id"] = new BsonDocument("$gt", "report-plan-00199")
                },
                ["sort"] = new BsonDocument("_id", 1),
                ["limit"] = 200
            },
            ["verbosity"] = "executionStats"
        };
        _ = await _database.RunCommandAsync<BsonDocument>(explainCommand);
        var explain = await _database.RunCommandAsync<BsonDocument>(explainCommand);
        var plan = explain.ToJson();
        var execution = explain["executionStats"].AsBsonDocument;

        Assert.Contains("ix_workitems_project_archived_id", plan, StringComparison.Ordinal);
        Assert.DoesNotContain("COLLSCAN", plan, StringComparison.Ordinal);
        Assert.InRange(execution["totalDocsExamined"].ToInt64(), 1, 200);
        Assert.InRange(execution["executionTimeMillis"].ToInt64(), 0, 250);

        var workLogs = _database.GetCollection<BsonDocument>("workitemworklogactivitys");
        await workLogs.InsertManyAsync(Enumerable.Range(0, 5_000).Select(index => new BsonDocument
        {
            ["_id"] = $"report-log-{index:D5}",
            ["OrganizationId"] = "report-org",
            ["ProjectId"] = index < 1_000 ? "report-target" : "report-other",
            ["WorkItemId"] = $"report-plan-{index:D5}",
            ["Hours"] = 1m,
            ["Version"] = 1L
        }));
        var activityExplainCommand = new BsonDocument
        {
            ["explain"] = new BsonDocument
            {
                ["find"] = "workitemworklogactivitys",
                ["filter"] = new BsonDocument
                {
                    ["OrganizationId"] = "report-org",
                    ["ProjectId"] = "report-target",
                    ["_id"] = new BsonDocument("$gt", "report-log-00199")
                },
                ["sort"] = new BsonDocument("_id", 1),
                ["limit"] = 200
            },
            ["verbosity"] = "executionStats"
        };
        _ = await _database.RunCommandAsync<BsonDocument>(activityExplainCommand);
        var activityExplain = await _database.RunCommandAsync<BsonDocument>(activityExplainCommand);
        var activityPlan = activityExplain.ToJson();
        var activityExecution = activityExplain["executionStats"].AsBsonDocument;

        Assert.Contains("ix_workitem_worklogs_project_cursor", activityPlan, StringComparison.Ordinal);
        Assert.DoesNotContain("COLLSCAN", activityPlan, StringComparison.Ordinal);
        Assert.InRange(activityExecution["totalDocsExamined"].ToInt64(), 1, 200);
        Assert.InRange(activityExecution["executionTimeMillis"].ToInt64(), 0, 250);
    }

    [Fact]
    public async Task PrivacyWorkflowIndexes_AreTenantScopedAndIdempotent()
    {
        var runner = CreateRunner(new MongoMigrationOptions { BatchSize = 10, MaxBatchesPerRun = 20 });
        var firstReport = await runner.RunAsync();
        var secondReport = await runner.RunAsync();
        var first = Outcome(firstReport, MongoMigrationRunner.PrivacyWorkflowIndexMigrationId);
        var second = Outcome(secondReport, MongoMigrationRunner.PrivacyWorkflowIndexMigrationId);
        var firstUtc = Outcome(firstReport, MongoMigrationRunner.PrivacyWorkflowUtcIndexMigrationId);
        var secondUtc = Outcome(secondReport, MongoMigrationRunner.PrivacyWorkflowUtcIndexMigrationId);
        Assert.Equal(MongoMigrationStates.Completed, first.Status);
        Assert.Equal(MongoPrivacyWorkflowIndexes.All.Count, first.Examined);
        Assert.Equal(MongoMigrationStates.Skipped, second.Status);
        Assert.Equal(MongoMigrationStates.Completed, firstUtc.Status);
        Assert.Equal(MongoPrivacyWorkflowUtcIndexes.All.Count, firstUtc.Examined);
        Assert.Equal(MongoMigrationStates.Skipped, secondUtc.Status);

        foreach (var specification in MongoPrivacyWorkflowIndexes.All)
        {
            var collection = _database.GetCollection<BsonDocument>(specification.Collection);
            using var cursor = await collection.Indexes.ListAsync();
            var actual = Assert.Single(
                await cursor.ToListAsync(),
                index => index["name"] == specification.Name);
            Assert.Equal(specification.Keys, actual["key"].AsBsonDocument);
        }
        foreach (var specification in MongoPrivacyWorkflowUtcIndexes.All)
        {
            var collection = _database.GetCollection<BsonDocument>(specification.Collection);
            using var cursor = await collection.Indexes.ListAsync();
            var actual = Assert.Single(
                await cursor.ToListAsync(),
                index => index["name"] == specification.Name);
            Assert.Equal(specification.Keys, actual["key"].AsBsonDocument);
        }
    }

    [Fact]
    public async Task NotificationDeliveryIndexes_ReplaceGlobalDedupeAndAreIdempotent()
    {
        var catalog = MongoNotificationDeliveryIndexes.All;
        Assert.Equal(2, catalog.Count);
        var runner = CreateRunner(new MongoMigrationOptions { BatchSize = 10, MaxBatchesPerRun = 20 });
        var first = Outcome(await runner.RunAsync(), MongoMigrationRunner.NotificationDeliveryIndexMigrationId);
        var second = Outcome(await runner.RunAsync(), MongoMigrationRunner.NotificationDeliveryIndexMigrationId);
        Assert.Equal(MongoMigrationStates.Completed, first.Status);
        Assert.Equal(catalog.Count, first.Examined);
        Assert.Equal(MongoMigrationStates.Skipped, second.Status);
        foreach (var specification in catalog)
        {
            var collection = _database.GetCollection<BsonDocument>(specification.Collection);
            using var cursor = await collection.Indexes.ListAsync();
            var actual = Assert.Single(await cursor.ToListAsync(), index => index["name"] == specification.Name);
            Assert.Equal(specification.Keys, actual["key"].AsBsonDocument);
            Assert.Equal(specification.Unique, actual.GetValue("unique", false).ToBoolean());
        }

        var notifications = _database.GetCollection<BsonDocument>("notifications");
        await notifications.InsertOneAsync(new BsonDocument
        {
            ["_id"] = "notification-dedupe-org-1-a",
            ["OrganizationId"] = "org-1",
            ["DeduplicationKey"] = "shared-dedupe"
        });
        var duplicate = await Assert.ThrowsAsync<MongoWriteException>(() =>
            notifications.InsertOneAsync(new BsonDocument
            {
                ["_id"] = "notification-dedupe-org-1-b",
                ["OrganizationId"] = "org-1",
                ["DeduplicationKey"] = "shared-dedupe"
            }));
        Assert.Equal(ServerErrorCategory.DuplicateKey, duplicate.WriteError.Category);
        await notifications.InsertOneAsync(new BsonDocument
        {
            ["_id"] = "notification-dedupe-org-2",
            ["OrganizationId"] = "org-2",
            ["DeduplicationKey"] = "shared-dedupe"
        });
    }

    [Fact]
    public async Task WebhookIndexes_AreTenantScopedAndIdempotent()
    {
        var catalog = MongoWebhookIndexes.All;
        Assert.Equal(4, catalog.Count);
        var runner = CreateRunner(new MongoMigrationOptions { BatchSize = 10, MaxBatchesPerRun = 20 });
        var first = Outcome(await runner.RunAsync(), MongoMigrationRunner.WebhookIndexMigrationId);
        var second = Outcome(await runner.RunAsync(), MongoMigrationRunner.WebhookIndexMigrationId);
        Assert.Equal(MongoMigrationStates.Completed, first.Status);
        Assert.Equal(catalog.Count, first.Examined);
        Assert.Equal(MongoMigrationStates.Skipped, second.Status);
        foreach (var specification in catalog)
        {
            var collection = _database.GetCollection<BsonDocument>(specification.Collection);
            using var cursor = await collection.Indexes.ListAsync();
            var actual = Assert.Single(
                await cursor.ToListAsync(),
                index => index["name"] == specification.Name);
            Assert.Equal(specification.Keys, actual["key"].AsBsonDocument);
        }
    }

    [Fact]
    public async Task IntakeIndexes_AreTenantScopedUniqueAndIdempotent()
    {
        var catalog = MongoIntakeIndexes.All;
        Assert.Equal(6, catalog.Count);
        Assert.Equal(
            catalog.Count,
            catalog.Select(x => (x.Module, x.Collection, x.Name)).Distinct().Count());
        var runner = CreateRunner(new MongoMigrationOptions
        {
            BatchSize = 10,
            MaxBatchesPerRun = 20
        });
        var first = Outcome(
            await runner.RunAsync(),
            MongoMigrationRunner.IntakeIndexMigrationId);
        var second = Outcome(
            await runner.RunAsync(),
            MongoMigrationRunner.IntakeIndexMigrationId);
        Assert.Equal(MongoMigrationStates.Completed, first.Status);
        Assert.Equal(catalog.Count, first.Examined);
        Assert.Equal(MongoMigrationStates.Skipped, second.Status);
        foreach (var specification in catalog)
        {
            var collection = _database.GetCollection<BsonDocument>(specification.Collection);
            using var cursor = await collection.Indexes.ListAsync();
            var actual = Assert.Single(
                await cursor.ToListAsync(),
                index => index["name"] == specification.Name);
            Assert.Equal(specification.Keys, actual["key"].AsBsonDocument);
        }
    }

    [Fact]
    public async Task IdentityCredentialIndexes_AreExactAndCreatedIdempotently()
    {
        var catalog = MongoIdentityCredentialIndexes.All;
        Assert.Equal(5, catalog.Count);
        Assert.Equal(catalog.Count, catalog.Select(x => (x.Module, x.Collection, x.Name)).Distinct().Count());

        var runner = CreateRunner(new MongoMigrationOptions { BatchSize = 10, MaxBatchesPerRun = 20 });
        var first = Outcome(
            await runner.RunAsync(CancellationToken.None),
            MongoMigrationRunner.IdentityCredentialIndexMigrationId);
        var second = Outcome(
            await runner.RunAsync(CancellationToken.None),
            MongoMigrationRunner.IdentityCredentialIndexMigrationId);
        var scalarUtc = Outcome(
            await runner.RunAsync(CancellationToken.None),
            MongoMigrationRunner.IdentityCredentialScalarUtcIndexMigrationId);

        Assert.Equal(MongoMigrationStates.Completed, first.Status);
        Assert.Equal(catalog.Count, first.Examined);
        Assert.Equal(MongoMigrationStates.Skipped, second.Status);
        Assert.Equal(MongoMigrationStates.Skipped, scalarUtc.Status);
        Assert.Equal(2, scalarUtc.Examined);

        foreach (var specification in catalog)
        {
            var collection = _database.GetCollection<BsonDocument>(specification.Collection);
            using var cursor = await collection.Indexes.ListAsync();
            var indexes = await cursor.ToListAsync();
            var actual = Assert.Single(indexes, x => x["name"].AsString == specification.Name);
            Assert.Equal(specification.Keys, actual["key"].AsBsonDocument);
            Assert.Equal(specification.Unique, actual.GetValue("unique", false).ToBoolean());
            if (specification.ExpireAfter is null)
            {
                Assert.False(actual.Contains("expireAfterSeconds"));
            }
            else
            {
                Assert.Equal(
                    Convert.ToInt64(specification.ExpireAfter.Value.TotalSeconds),
                    actual["expireAfterSeconds"].ToInt64());
            }
        }
    }

    [Fact]
    public async Task RequiredIndexes_AcceptEquivalentLegacyNamesWithoutDroppingThem()
    {
        var users = _database.GetCollection<BsonDocument>("users");
        await users.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<BsonDocument>(
                new BsonDocument("Username", 1),
                new CreateIndexOptions
                {
                    Name = "Username_1",
                    Unique = true,
                    Collation = new Collation("en", strength: CollationStrength.Secondary)
                }),
            new CreateIndexModel<BsonDocument>(
                new BsonDocument("Email", 1),
                new CreateIndexOptions
                {
                    Name = "Email_1",
                    Unique = true,
                    Collation = new Collation("en", strength: CollationStrength.Secondary)
                })
        ]);

        var outcome = Outcome(
            await CreateRunner(new MongoMigrationOptions()).RunAsync(),
            MongoMigrationRunner.IndexMigrationId);
        using var cursor = await users.Indexes.ListAsync();
        var names = (await cursor.ToListAsync()).Select(index => index["name"].AsString).ToList();

        Assert.Equal(MongoMigrationStates.Completed, outcome.Status);
        Assert.Contains("Username_1", names);
        Assert.Contains("Email_1", names);
        Assert.DoesNotContain("ux_users_username_ci", names);
        Assert.DoesNotContain("ux_users_email_ci", names);
    }

    [Fact]
    public async Task IdentityCredentialBackfills_AreBoundedResumableIdempotentAndPreserveLegacyData()
    {
        var expiresAt = Utc(2030, 1, 1);
        var users = _database.GetCollection<BsonDocument>("users");
        await users.InsertManyAsync(Enumerable.Range(1, 3).Select(number => new BsonDocument
        {
            ["_id"] = $"user-{number}",
            ["Username"] = $"legacy-user-{number}",
            ["Email"] = $"legacy-{number}@example.test",
            ["OrganizationId"] = "org-data006",
            ["RefreshTokens"] = new BsonArray
            {
                new BsonDocument
                {
                    ["SessionId"] = $"session-{number}",
                    ["TokenHash"] = $"hash-{number}",
                    ["CreatedAt"] = new BsonArray { Utc(2029, 12, number).Ticks, 0 },
                    ["ExpiresAt"] = new BsonArray { expiresAt.Ticks, 0 },
                    ["RevokedAt"] = BsonNull.Value
                }
            }
        }));
        var apiKeys = _database.GetCollection<BsonDocument>("apikeys");
        await apiKeys.InsertManyAsync(Enumerable.Range(1, 3).Select(number => new BsonDocument
        {
            ["_id"] = $"legacy-key-{number}",
            ["UserId"] = $"user-{number}",
            ["OrganizationId"] = "org-data006",
            ["ExpiresAt"] = new BsonArray { expiresAt.Ticks, 0 },
            ["RevokedAt"] = BsonNull.Value
        }));

        var pausedRunner = CreateRunner(new MongoMigrationOptions
        {
            RunDataMigrations = true,
            BatchSize = 1,
            MaxBatchesPerRun = 1
        });
        var pausedReport = await pausedRunner.RunAsync(CancellationToken.None);
        var paused = Outcome(pausedReport, MongoMigrationRunner.RefreshSessionMigrationId);
        var apiKeyPaused = Outcome(pausedReport, MongoMigrationRunner.ApiKeyVersionMigrationId);

        Assert.Equal(MongoMigrationStates.Paused, paused.Status);
        Assert.Equal(1, paused.Examined);
        Assert.Equal(1, paused.Changed);
        Assert.Equal(MongoMigrationStates.Paused, apiKeyPaused.Status);
        Assert.Equal(1, apiKeyPaused.Changed);
        var sessions = _database.GetCollection<BsonDocument>("refreshsessions");
        Assert.Equal(1, await sessions.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));

        var resumedRunner = CreateRunner(new MongoMigrationOptions
        {
            RunDataMigrations = true,
            BatchSize = 1,
            MaxBatchesPerRun = 20
        });
        var resumedReport = await resumedRunner.RunAsync(CancellationToken.None);
        var resumed = Outcome(resumedReport, MongoMigrationRunner.RefreshSessionMigrationId);
        var apiKeyResumed = Outcome(resumedReport, MongoMigrationRunner.ApiKeyVersionMigrationId);
        var rerunReport = await resumedRunner.RunAsync(CancellationToken.None);
        var rerun = Outcome(rerunReport, MongoMigrationRunner.RefreshSessionMigrationId);
        var apiKeyRerun = Outcome(rerunReport, MongoMigrationRunner.ApiKeyVersionMigrationId);

        Assert.Equal(MongoMigrationStates.Completed, resumed.Status);
        Assert.Equal(3, resumed.Examined);
        Assert.Equal(3, resumed.Changed);
        Assert.Equal(MongoMigrationStates.Skipped, rerun.Status);
        Assert.Equal(MongoMigrationStates.Completed, apiKeyResumed.Status);
        Assert.Equal(3, apiKeyResumed.Changed);
        Assert.Equal(MongoMigrationStates.Skipped, apiKeyRerun.Status);
        Assert.Equal(3, await apiKeys.CountDocumentsAsync(Builders<BsonDocument>.Filter.Eq("Version", 1L)));
        Assert.Equal(3, await apiKeys.CountDocumentsAsync(
            Builders<BsonDocument>.Filter.Type("ExpiresAtUtc", BsonType.DateTime)));
        Assert.Equal(3, await sessions.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));
        Assert.Equal(3, await users.CountDocumentsAsync(
            Builders<BsonDocument>.Filter.Exists("RefreshTokens.0", true)));

        var first = await sessions.Find(Builders<BsonDocument>.Filter.Eq("_id", "session-1")).SingleAsync();
        Assert.Equal("user-1", first["UserId"].AsString);
        Assert.Equal("org-data006", first["OrganizationId"].AsString);
        Assert.Equal("hash-1", first["TokenHash"].AsString);
        Assert.Equal(expiresAt.AddDays(30), first["RetainUntilUtc"].ToUniversalTime());
        Assert.Equal(1, first["Version"].ToInt64());
    }

    [Fact]
    public async Task RefreshSessionBackfill_RejectsIncompatibleExistingSession()
    {
        var expiresAt = Utc(2030, 1, 1);
        await _database.GetCollection<BsonDocument>("users").InsertOneAsync(new BsonDocument
        {
            ["_id"] = "legacy-user",
            ["OrganizationId"] = "legacy-org",
            ["RefreshTokens"] = new BsonArray
            {
                new BsonDocument
                {
                    ["SessionId"] = "conflicting-session",
                    ["TokenHash"] = "legacy-hash",
                    ["CreatedAt"] = new BsonArray { Utc(2029, 12, 1).Ticks, 0 },
                    ["ExpiresAt"] = new BsonArray { expiresAt.Ticks, 0 }
                }
            }
        });
        await _database.GetCollection<BsonDocument>("refreshsessions").InsertOneAsync(new BsonDocument
        {
            ["_id"] = "conflicting-session",
            ["UserId"] = "different-user",
            ["OrganizationId"] = "legacy-org",
            ["TokenHash"] = "different-hash",
            ["Version"] = 1L
        });

        var runner = CreateRunner(new MongoMigrationOptions
        {
            RunDataMigrations = true,
            BatchSize = 10,
            MaxBatchesPerRun = 10
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.RunAsync(CancellationToken.None));

        Assert.Contains("conflicts with incompatible", exception.Message, StringComparison.Ordinal);
        var ledger = await ReadLedgerEntryAsync(MongoMigrationRunner.RefreshSessionMigrationId);
        Assert.NotEqual(MongoMigrationStates.Completed, ledger["State"].AsString);
    }

    [Fact]
    public async Task WorkflowLifecycleBackfill_AddsPublishedVersionHistoryAndIssueScheme()
    {
        var workflows = _database.GetCollection<BsonDocument>("workflows");
        await workflows.InsertOneAsync(new BsonDocument
        {
            ["_id"] = "legacy-workflow",
            ["ProjectId"] = "legacy-project",
            ["Statuses"] = new BsonArray
            {
                new BsonDocument { ["Name"] = "Open", ["Category"] = "Todo" },
                new BsonDocument { ["Name"] = "Done", ["Category"] = "Done" }
            },
            ["Transitions"] = new BsonArray
            {
                new BsonDocument { ["FromStatus"] = "Open", ["ToStatus"] = "Done" }
            },
            ["CreatedAt"] = Utc(2026, 7, 20),
            ["UpdatedAt"] = Utc(2026, 7, 20),
            ["Version"] = 0L
        });
        var runner = CreateRunner(new MongoMigrationOptions
        {
            RunDataMigrations = true,
            BatchSize = 1,
            MaxBatchesPerRun = 20
        });

        var outcome = Outcome(await runner.RunAsync(CancellationToken.None), MongoMigrationRunner.WorkflowLifecycleMigrationId);
        var migrated = await workflows.Find(new BsonDocument("_id", "legacy-workflow")).SingleAsync();

        Assert.Equal(MongoMigrationStates.Completed, outcome.Status);
        Assert.Equal(1, migrated["PublishedVersion"].AsInt32);
        Assert.True(migrated["Draft"].IsBsonNull);
        Assert.Single(migrated["PublishedVersions"].AsBsonArray);
        var scheme = Assert.Single(migrated["IssueTypeSchemes"].AsBsonArray).AsBsonDocument;
        Assert.Equal("*", scheme["IssueType"].AsString);
        Assert.Equal("Open", scheme["DefaultStatus"].AsString);
        Assert.Equal(1L, migrated["Version"].ToInt64());
    }

    [Fact]
    public async Task SprintLifecycleBackfill_IsBoundedIdempotentAndGroupsLegacyLabels()
    {
        var workItems = _database.GetCollection<BsonDocument>(WorkItemsCollection);
        await workItems.InsertManyAsync(
        [
            new BsonDocument
            {
                ["_id"] = "legacy-sprint-item-a",
                ["ProjectId"] = "legacy-sprint-project",
                ["SprintId"] = "Sprint 42",
                ["Version"] = 2L
            },
            new BsonDocument
            {
                ["_id"] = "legacy-sprint-item-b",
                ["ProjectId"] = "legacy-sprint-project",
                ["SprintId"] = "Sprint 42",
                ["Version"] = 0L
            }
        ]);
        var runner = CreateRunner(new MongoMigrationOptions
        {
            RunDataMigrations = true,
            BatchSize = 1,
            MaxBatchesPerRun = 20
        });

        var first = Outcome(
            await runner.RunAsync(CancellationToken.None),
            MongoMigrationRunner.SprintLifecycleMigrationId);
        var second = Outcome(
            await runner.RunAsync(CancellationToken.None),
            MongoMigrationRunner.SprintLifecycleMigrationId);
        var migrated = await workItems.Find(
                Builders<BsonDocument>.Filter.In("_id", new[] { "legacy-sprint-item-a", "legacy-sprint-item-b" }))
            .Sort(Builders<BsonDocument>.Sort.Ascending("_id"))
            .ToListAsync();
        var sprintId = migrated[0]["SprintId"].AsString;
        var sprint = await _database.GetCollection<BsonDocument>("sprints")
            .Find(Builders<BsonDocument>.Filter.Eq("_id", sprintId))
            .SingleAsync();

        Assert.Equal(MongoMigrationStates.Completed, first.Status);
        Assert.Equal(2, first.Changed);
        Assert.Equal(MongoMigrationStates.Skipped, second.Status);
        Assert.StartsWith("legacy-", sprintId, StringComparison.Ordinal);
        Assert.Equal(39, sprintId.Length);
        Assert.All(migrated, item =>
        {
            Assert.Equal(sprintId, item["SprintId"].AsString);
            Assert.Equal(MongoMigrationRunner.SprintLifecycleMigrationId, item["SprintLifecycleMigratedBy"].AsString);
        });
        Assert.Equal(4L, migrated[0]["Version"].ToInt64());
        Assert.Equal(2L, migrated[1]["Version"].ToInt64());
        Assert.Equal("Planned", sprint["Status"].AsString);
    }

    [Fact]
    public async Task WorkItemTypeSchemaBackfill_CreatesProjectSchemaAndIsIdempotent()
    {
        await InsertWorkItemsAsync(
            new BsonDocument
            {
                ["_id"] = "legacy-schema-a",
                ["ProjectId"] = "legacy-schema-project",
                ["Type"] = "Task",
                ["Version"] = 0L
            },
            new BsonDocument
            {
                ["_id"] = "legacy-schema-b",
                ["ProjectId"] = "legacy-schema-project",
                ["Type"] = "Incident",
                ["Version"] = 0L
            });
        var runner = CreateRunner(new MongoMigrationOptions
        {
            RunDataMigrations = true,
            BatchSize = 1,
            MaxBatchesPerRun = 20
        });

        var first = Outcome(
            await runner.RunAsync(CancellationToken.None),
            MongoMigrationRunner.WorkItemTypeSchemaMigrationId);
        var second = Outcome(
            await runner.RunAsync(CancellationToken.None),
            MongoMigrationRunner.WorkItemTypeSchemaMigrationId);
        var migrated = await ReadWorkItemsAsync();
        var schema = await _database.GetCollection<BsonDocument>("workitemtypeschemas")
            .Find(Builders<BsonDocument>.Filter.Eq("ProjectId", "legacy-schema-project"))
            .SingleAsync();

        Assert.Equal(MongoMigrationStates.Completed, first.Status);
        Assert.Equal(2, first.Changed);
        Assert.Equal(MongoMigrationStates.Skipped, second.Status);
        Assert.All(migrated, item =>
        {
            Assert.Equal(1, item["IssueTypeSchemaVersion"].AsInt32);
            Assert.Empty(item["CustomFields"].AsBsonArray);
            Assert.Equal(MongoMigrationRunner.WorkItemTypeSchemaMigrationId,
                item["WorkItemTypeSchemaMigratedBy"].AsString);
        });
        Assert.Contains(schema["IssueTypes"].AsBsonArray,
            value => value.AsBsonDocument["Key"].AsString == "Incident");
        Assert.Equal(2, schema["SchemaVersion"].AsInt32);
    }

    [Fact]
    public async Task WorkItemGraphBackfill_IsProviderDeterministicAndUsesDependencyIndex()
    {
        await InsertWorkItemsAsync(
            new BsonDocument
            {
                ["_id"] = "graph-source",
                ["ProjectId"] = "graph-project",
                ["Type"] = "Task",
                ["IssueTypeSchemaVersion"] = 1,
                ["CustomFields"] = new BsonArray(),
                ["Relations"] = new BsonArray
                {
                    new BsonDocument
                    {
                        ["RelatedWorkItemId"] = "graph-target",
                        ["RelationType"] = "Blocks"
                    },
                    new BsonDocument
                    {
                        ["RelatedWorkItemId"] = "graph-peer",
                        ["RelationType"] = "RelatesTo"
                    }
                },
                ["Version"] = 0L
            },
            new BsonDocument
            {
                ["_id"] = "graph-target",
                ["ProjectId"] = "graph-project",
                ["Type"] = "Task",
                ["IssueTypeSchemaVersion"] = 1,
                ["CustomFields"] = new BsonArray(),
                ["Relations"] = new BsonArray(),
                ["Version"] = 0L
            });
        var runner = CreateRunner(new MongoMigrationOptions
        {
            RunDataMigrations = true,
            BatchSize = 1,
            MaxBatchesPerRun = 20
        });

        var first = Outcome(
            await runner.RunAsync(CancellationToken.None),
            MongoMigrationRunner.WorkItemGraphMigrationId);
        var second = Outcome(
            await runner.RunAsync(CancellationToken.None),
            MongoMigrationRunner.WorkItemGraphMigrationId);
        var edges = _database.GetCollection<BsonDocument>("workitemrelationedges");
        var dependency = await edges.Find(Builders<BsonDocument>.Filter.Eq(
                "_id",
                WorkItemGraphService.EdgeId("graph-project", "graph-source", "graph-target", "Blocks")))
            .SingleAsync();

        Assert.Equal(MongoMigrationStates.Completed, first.Status);
        Assert.Equal(2, first.Changed);
        Assert.Equal(MongoMigrationStates.Skipped, second.Status);
        Assert.Equal("graph-source", dependency["DependencyFromWorkItemId"].AsString);
        Assert.Equal("graph-target", dependency["DependencyToWorkItemId"].AsString);
        Assert.Equal(4, MongoWorkItemGraphIndexes.All.Count);

        var planDocuments = Enumerable.Range(0, 2_000).Select(index => new BsonDocument
        {
            ["_id"] = $"plan-edge-{index:D4}",
            ["ProjectId"] = "plan-project",
            ["SourceWorkItemId"] = $"source-{index:D4}",
            ["TargetWorkItemId"] = $"target-{index:D4}",
            ["RelationType"] = "Blocks",
            ["DependencyFromWorkItemId"] = index == 1_337 ? "needle" : $"source-{index:D4}",
            ["DependencyToWorkItemId"] = index == 1_555 ? "blocked-needle" : $"target-{index:D4}",
            ["CreatedAt"] = DateTime.UtcNow,
            ["Version"] = 0L
        });
        await edges.InsertManyAsync(planDocuments);

        var explain = await _database.RunCommandAsync<BsonDocument>(new BsonDocument
        {
            ["explain"] = new BsonDocument
            {
                ["find"] = "workitemrelationedges",
                ["filter"] = new BsonDocument
                {
                    ["ProjectId"] = "plan-project",
                    ["DependencyFromWorkItemId"] = "needle"
                }
            },
            ["verbosity"] = "queryPlanner"
        });
        var serializedPlan = explain.ToJson();

        var reverseExplain = await _database.RunCommandAsync<BsonDocument>(new BsonDocument
        {
            ["explain"] = new BsonDocument
            {
                ["find"] = "workitemrelationedges",
                ["filter"] = new BsonDocument
                {
                    ["ProjectId"] = "plan-project",
                    ["DependencyToWorkItemId"] = "blocked-needle"
                }
            },
            ["verbosity"] = "queryPlanner"
        });
        var planWorkItems = Enumerable.Range(0, 2_000).Select(index => new BsonDocument
        {
            ["_id"] = $"plan-workitem-{index:D4}",
            ["ProjectId"] = "plan-project",
            ["ParentId"] = index == 1_777 ? "parent-needle" : $"parent-{index:D4}",
            ["Archived"] = false,
            ["Version"] = 0L
        });
        await _database.GetCollection<BsonDocument>(WorkItemsCollection).InsertManyAsync(planWorkItems);
        var parentExplain = await _database.RunCommandAsync<BsonDocument>(new BsonDocument
        {
            ["explain"] = new BsonDocument
            {
                ["find"] = WorkItemsCollection,
                ["filter"] = new BsonDocument
                {
                    ["ProjectId"] = "plan-project",
                    ["ParentId"] = "parent-needle",
                    ["Archived"] = false
                }
            },
            ["verbosity"] = "queryPlanner"
        });
        var reversePlan = reverseExplain.ToJson();
        var parentPlan = parentExplain.ToJson();

        Assert.Contains("ix_workitem_relation_edges_dependency_from", serializedPlan, StringComparison.Ordinal);
        Assert.Contains("ix_workitem_relation_edges_dependency_to", reversePlan, StringComparison.Ordinal);
        Assert.Contains("ix_workitems_project_parent_archived", parentPlan, StringComparison.Ordinal);
        Assert.DoesNotContain("COLLSCAN", serializedPlan, StringComparison.Ordinal);
        Assert.DoesNotContain("COLLSCAN", reversePlan, StringComparison.Ordinal);
        Assert.DoesNotContain("COLLSCAN", parentPlan, StringComparison.Ordinal);
    }

    private async Task InsertWorkItemsAsync(params BsonDocument[] documents) =>
        await _database.GetCollection<BsonDocument>(WorkItemsCollection).InsertManyAsync(documents);

    private async Task<IReadOnlyList<BsonDocument>> ReadWorkItemsAsync() =>
        await _database.GetCollection<BsonDocument>(WorkItemsCollection)
            .Find(FilterDefinition<BsonDocument>.Empty)
            .Sort(Builders<BsonDocument>.Sort.Ascending("_id"))
            .ToListAsync();

    private async Task<long> CountPositiveRanksAsync() =>
        await _database.GetCollection<BsonDocument>(WorkItemsCollection).CountDocumentsAsync(
            Builders<BsonDocument>.Filter.Gt("Rank", 0));

    private async Task<BsonDocument> ReadLedgerEntryAsync(string migrationId) =>
        await _database.GetCollection<BsonDocument>(LedgerCollection)
            .Find(Builders<BsonDocument>.Filter.Eq("_id", migrationId))
            .SingleAsync();

    private MongoMigrationRunner CreateRunner(MongoMigrationOptions options)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MongoDb:ConnectionString"] = _connectionString,
                ["MongoDb:DatabaseName"] = _database.DatabaseNamespace.DatabaseName
            })
            .Build();
        return new MongoMigrationRunner(
            new MongoDbService(configuration),
            Options.Create(options),
            NullLogger<MongoMigrationRunner>.Instance);
    }

    private static MongoMigrationOutcome Outcome(MongoMigrationRunReport report, string migrationId) =>
        Assert.Single(report.Outcomes, x => x.MigrationId == migrationId);

    private async Task<string> SnapshotDatabaseAsync()
    {
        var names = (await (await _database.ListCollectionNamesAsync()).ToListAsync())
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();
        var snapshot = new BsonDocument();
        foreach (var name in names)
        {
            var collection = _database.GetCollection<BsonDocument>(name);
            var documents = await collection.Find(FilterDefinition<BsonDocument>.Empty)
                .Sort(Builders<BsonDocument>.Sort.Ascending("_id"))
                .ToListAsync();
            using var indexCursor = await collection.Indexes.ListAsync();
            var indexes = (await indexCursor.ToListAsync())
                .OrderBy(x => x["name"].AsString, StringComparer.Ordinal)
                .ToList();
            snapshot[name] = new BsonDocument
            {
                ["documents"] = new BsonArray(documents),
                ["indexes"] = new BsonArray(indexes)
            };
        }

        return snapshot.ToJson();
    }

    private static BsonDocument WorkItem(
        string id,
        DateTime createdAt,
        long? rank = null,
        BsonValue? rankValue = null)
    {
        var document = new BsonDocument
        {
            ["_id"] = id,
            ["ProjectId"] = "project-data003",
            ["BoardId"] = "board-data003",
            ["ColumnId"] = "column-data003",
            ["Archived"] = false,
            ["Status"] = "To Do",
            ["Type"] = "Task",
            ["IssueTypeSchemaVersion"] = 1,
            ["CustomFields"] = new BsonArray(),
            ["CreatedAt"] = createdAt
        };
        if (rank is not null)
        {
            document["Rank"] = rank.Value;
        }
        else if (rankValue is not null)
        {
            document["Rank"] = rankValue;
        }

        return document;
    }

    private static DateTime Utc(int year, int month, int day) =>
        new(year, month, day, 0, 0, 0, DateTimeKind.Utc);

    private static IReadOnlyList<ExpectedIndex> RequiredIndexes() =>
    [
        Index("Identity", "users", "ux_users_username_ci", Keys(("Username", 1)), unique: true, caseInsensitive: true),
        Index("Identity", "users", "ux_users_email_ci", Keys(("Email", 1)), unique: true, caseInsensitive: true),
        Index("Identity", "users", "ix_users_active_username", Keys(("IsActive", 1), ("Username", 1), ("_id", 1))),
        Index("Identity", "users", "ix_users_organization_active_username", Keys(("OrganizationId", 1), ("IsActive", 1), ("Username", 1), ("_id", 1))),
        Index("Identity", "users", "ix_users_refresh_token_hash", Keys(("RefreshTokens.TokenHash", 1))),
        Index("Identity", "users", "ux_users_password_reset_token_hash", Keys(("PasswordResetTokenHash", 1)), unique: true, partialFilter: new BsonDocument("PasswordResetTokenHash", new BsonDocument("$type", "string"))),
        Index("Identity", "users", "ix_users_active_roles", Keys(("IsActive", 1), ("Roles", 1), ("_id", 1))),
        Index("Identity", "identityroles", "ux_identityroles_organization_name_ci", Keys(("OrganizationId", 1), ("Name", 1)), unique: true, caseInsensitive: true),
        Index("Identity", "identityroles", "ix_identityroles_system_organization_name", Keys(("IsSystem", 1), ("OrganizationId", 1), ("Name", 1), ("_id", 1))),
        Index("Identity", "apikeys", "ix_apikeys_user_created", Keys(("UserId", 1), ("CreatedAt", -1), ("_id", 1))),
        Index("Identity", "apikeys", "ttl_apikeys_expires_utc", Keys(("ExpiresAtUtc", 1)), expireAfter: TimeSpan.Zero),
        Index("Organizations", "organizations", "ux_organizations_tenant_key_ci", Keys(("TenantKey", 1)), unique: true, caseInsensitive: true),
        Index("Organizations", "organizations", "ix_organizations_name", Keys(("Name", 1), ("_id", 1))),
        Index("Projects", "projects", "ux_projects_organization_key_ci", Keys(("OrganizationId", 1), ("Key", 1)), unique: true, caseInsensitive: true),
        Index("Projects", "projects", "ix_projects_organization_archived_key", Keys(("OrganizationId", 1), ("Archived", 1), ("Key", 1), ("_id", 1))),
        Index("Teams", "teams", "ux_teams_organization_name_ci", Keys(("OrganizationId", 1), ("Name", 1)), unique: true, caseInsensitive: true),
        Index("Teams", "teams", "ix_teams_organization_archived_name", Keys(("OrganizationId", 1), ("Archived", 1), ("Name", 1), ("_id", 1))),
        Index("Boards", "boards", "ux_boards_active_project_name_ci", Keys(("ProjectId", 1), ("Name", 1)), unique: true, partialFilter: new BsonDocument("Archived", false), caseInsensitive: true),
        Index("Boards", "boards", "ix_boards_project_archived_name", Keys(("ProjectId", 1), ("Archived", 1), ("Name", 1), ("_id", 1))),
        Index("Workflows", "workflows", "ux_workflows_project", Keys(("ProjectId", 1)), unique: true),
        Index("WorkItems", "workitems", "ix_workitems_project_archived_created", Keys(("ProjectId", 1), ("Archived", 1), ("CreatedAt", -1), ("_id", 1))),
        Index("WorkItems", "workitems", "ix_workitems_board_column_archived_rank", Keys(("BoardId", 1), ("ColumnId", 1), ("Archived", 1), ("Rank", 1), ("_id", 1))),
        Index("WorkItems", "workitems", "ix_workitems_project_archived_status_rank", Keys(("ProjectId", 1), ("Archived", 1), ("Status", 1), ("Rank", 1), ("_id", 1))),
        Index("WorkItems", "workitems", "ix_workitems_project_archived_type_rank", Keys(("ProjectId", 1), ("Archived", 1), ("Type", 1), ("Rank", 1), ("_id", 1))),
        Index("WorkItems", "workitems", "ix_workitems_project_archived_custom_field", Keys(("ProjectId", 1), ("Archived", 1), ("CustomFields.FieldKey", 1), ("CustomFields.SearchValue", 1), ("_id", 1))),
        Index("WorkItems", "workitemtypeschemas", "ux_workitem_type_schemas_project", Keys(("ProjectId", 1)), unique: true),
        Index("WorkItems", "boardcolumnwipprojections", "ix_wip_projection_project_board_column", Keys(("ProjectId", 1), ("BoardId", 1), ("ColumnId", 1))),
        Index("WorkItems", "workitems", "ix_workitems_project_archived_rank", Keys(("ProjectId", 1), ("Archived", 1), ("Rank", 1), ("_id", 1))),
        Index("WorkItems", "workitems", "ix_workitems_parent_archived", Keys(("ParentId", 1), ("Archived", 1))),
        Index("WorkItems", "workitems", "ix_workitems_project_archived_assignee_rank", Keys(("ProjectId", 1), ("Archived", 1), ("AssigneeUserId", 1), ("Rank", 1), ("_id", 1))),
        Index("WorkItems", "workitems", "ix_workitems_project_archived_due", Keys(("ProjectId", 1), ("Archived", 1), ("DueDate", 1), ("_id", 1))),
        Index("WorkItems", "workitems", "ix_workitems_project_archived_sprint", Keys(("ProjectId", 1), ("Archived", 1), ("SprintId", 1))),
        Index("WorkItems", "workitems", "ix_workitems_project_archived_completed", Keys(("ProjectId", 1), ("Archived", 1), ("CompletedAt", 1))),
        Index("WorkItems", "workitems", "ix_workitems_due_reminder", Keys(("Archived", 1), ("DueDate", 1), ("_id", 1))),
        Index("WorkItems", "sprints", "ux_sprints_project_name_ci", Keys(("ProjectId", 1), ("Name", 1)), unique: true, caseInsensitive: true),
        Index("WorkItems", "sprints", "ux_sprints_active_project", Keys(("ProjectId", 1)), unique: true, partialFilter: new BsonDocument("Status", "Active")),
        Index("WorkItems", "sprints", "ix_sprints_project_status_start", Keys(("ProjectId", 1), ("Status", 1), ("StartAtUtc", -1), ("_id", 1))),
        Index("WorkItems", "sprintscopesnapshots", "ix_sprint_scope_sprint_item", Keys(("SprintId", 1), ("WorkItemId", 1))),
        Index("WorkItems", "sprintcompletionsnapshots", "ix_sprint_completion_sprint_item", Keys(("SprintId", 1), ("WorkItemId", 1))),
        Index("Audit", "auditlogs", "ix_auditlogs_entity_created", Keys(("EntityType", 1), ("EntityId", 1), ("CreatedAt", -1))),
        Index("Audit", "auditlogs", "ix_auditlogs_actor_created", Keys(("ActorUserId", 1), ("CreatedAt", -1))),
        Index("Audit", "auditlogs", "ix_auditlogs_action_created", Keys(("Action", 1), ("CreatedAt", -1))),
        Index("Audit", "auditlogs", "ix_auditlogs_created", Keys(("CreatedAt", -1), ("_id", 1))),
        Index("Notifications", "notifications", "ix_notifications_user_read_created", Keys(("UserId", 1), ("Read", 1), ("CreatedAt", -1), ("_id", 1))),
        Index(
            "Notifications",
            "notifications",
            "ux_notifications_deduplication_key",
            Keys(("DeduplicationKey", 1)),
            unique: true,
            partialFilter: new BsonDocument("DeduplicationKey", new BsonDocument("$type", "string"))),
        Index("Notifications", "notifications", "ix_notifications_email_status_next_attempt", Keys(("EmailStatus", 1), ("EmailNextAttemptAt", 1))),
        Index("Notifications", "notificationpreferences", "ux_notificationpreferences_user", Keys(("UserId", 1)), unique: true)
    ];

    private static BsonDocument Keys(params (string Field, object Direction)[] keys)
    {
        var document = new BsonDocument();
        foreach (var (field, direction) in keys)
        {
            document[field] = BsonValue.Create(direction);
        }

        return document;
    }

    private static ExpectedIndex Index(
        string module,
        string collectionName,
        string name,
        BsonDocument keys,
        bool unique = false,
        TimeSpan? expireAfter = null,
        BsonDocument? partialFilter = null,
        bool caseInsensitive = false) =>
        new(module, collectionName, name, keys, unique, expireAfter, partialFilter, caseInsensitive);

    private sealed record ExpectedIndex(
        string Module,
        string CollectionName,
        string Name,
        BsonDocument Keys,
        bool Unique,
        TimeSpan? ExpireAfter,
        BsonDocument? PartialFilter,
        bool CaseInsensitive);
}
