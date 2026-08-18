using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.Api.Infrastructure.Persistence.MongoDb;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.WorkItems;

namespace Zumbo.PersistenceIntegrationTests;

public sealed class MongoWorkItemRecurrenceOccurrenceScheduleIndexTests : IAsyncLifetime
{
    private readonly MongoDbService mongo;
    private readonly string databaseName;
    private readonly IMongoCollection<BsonDocument> rawCollection;
    private readonly MongoRepository<WorkItemRecurrenceOccurrenceDocument> occurrences;

    public MongoWorkItemRecurrenceOccurrenceScheduleIndexTests()
    {
        WorkItemRecurrenceOccurrenceBsonConfiguration.EnsureRegistered();

        var connectionString = Environment.GetEnvironmentVariable("ZUMBO_TEST_MONGO_CONNECTION_STRING")
            ?? throw new InvalidOperationException("ZUMBO_TEST_MONGO_CONNECTION_STRING is required.");
        databaseName = "ZumboOccScheduleIndex_" + Guid.NewGuid().ToString("N");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MongoDb:ConnectionString"] = connectionString,
            ["MongoDb:DatabaseName"] = databaseName,
            ["Modules:WorkItems:MongoDb:DatabaseName"] = databaseName
        }).Build();
        mongo = new MongoDbService(configuration);
        rawCollection = mongo.GetDatabase("WorkItems").GetCollection<BsonDocument>("workitemrecurrenceoccurrences");
        occurrences = new MongoRepository<WorkItemRecurrenceOccurrenceDocument>(mongo);
    }

    public async Task InitializeAsync()
    {
        await rawCollection.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(
            new BsonDocument
            {
                ["RecurrenceId"] = 1,
                ["ScheduledForUtc"] = 1
            },
            new CreateIndexOptions
            {
                Name = "ux_workitem_recurrence_occurrence_schedule",
                Unique = true
            }));
    }

    public Task DisposeAsync() => mongo.GetDatabase("WorkItems").Client.DropDatabaseAsync(databaseName);

    [Fact]
    public async Task TwoOccurrencesForSameRecurrence_DifferentSchedules_Coexist()
    {
        const string recurrenceId = "recurrence-schedule-a";
        var t1 = new DateTimeOffset(2026, 7, 23, 13, 46, 0, TimeSpan.Zero);
        var t2 = new DateTimeOffset(2026, 7, 30, 13, 46, 0, TimeSpan.Zero);

        await occurrences.CreateAsync(Occurrence(recurrenceId, t1));
        await occurrences.CreateAsync(Occurrence(recurrenceId, t2));

        var stored = await occurrences.ListByFilterAsync(
            x => x.RecurrenceId == recurrenceId,
            x => x.ScheduledForUtc);
        Assert.Equal(2, stored.Count);
        Assert.Equal(t1, stored[0].ScheduledForUtc);
        Assert.Equal(t2, stored[1].ScheduledForUtc);
    }

    [Fact]
    public async Task SameSchedule_SameRecurrence_IsRejectedAsConflict()
    {
        const string recurrenceId = "recurrence-schedule-b";
        var t1 = new DateTimeOffset(2026, 7, 23, 13, 46, 0, TimeSpan.Zero);
        await occurrences.CreateAsync(Occurrence(recurrenceId, t1));

        var duplicate = Occurrence(recurrenceId, t1);
        duplicate.Id = "different-id-" + Guid.NewGuid().ToString("N");

        await Assert.ThrowsAsync<DocumentConflictException>(() => occurrences.CreateAsync(duplicate));
    }

    [Fact]
    public async Task NewScalarOccurrence_CoexistsWithLegacyArrayOccurrence_ForSameRecurrence()
    {
        const string recurrenceId = "recurrence-schedule-c";
        var legacyTime = new DateTimeOffset(2026, 7, 23, 13, 46, 0, TimeSpan.Zero);
        var newTime = new DateTimeOffset(2026, 7, 30, 13, 46, 0, TimeSpan.Zero);

        await rawCollection.InsertOneAsync(new BsonDocument
        {
            ["_id"] = "legacy-array-occurrence",
            ["OrganizationId"] = "org-1",
            ["ProjectId"] = "project-1",
            ["RecurrenceId"] = recurrenceId,
            ["TemplateId"] = "template-1",
            ["ScheduledForUtc"] = new BsonArray
            {
                new BsonInt64(legacyTime.UtcTicks),
                new BsonInt32(0)
            },
            ["Status"] = "Generated",
            ["CreatedAt"] = new BsonArray
            {
                new BsonInt64(legacyTime.UtcTicks),
                new BsonInt32(0)
            },
            ["Version"] = new BsonInt64(0)
        });

        await occurrences.CreateAsync(Occurrence(recurrenceId, newTime));

        var stored = await occurrences.ListByFilterAsync(
            x => x.RecurrenceId == recurrenceId,
            x => x.ScheduledForUtc);
        Assert.Equal(2, stored.Count);

        var legacy = await occurrences.SelectAsync(
            x => x.RecurrenceId == recurrenceId && x.ScheduledForUtc == legacyTime);
        Assert.NotNull(legacy);
        Assert.Equal("legacy-array-occurrence", legacy.Id);
        Assert.Equal(legacyTime, legacy.ScheduledForUtc);
    }

    private static WorkItemRecurrenceOccurrenceDocument Occurrence(string recurrenceId, DateTimeOffset scheduledFor) => new()
    {
        Id = WorkItemTemplateRecurrenceService.StableOccurrenceId(recurrenceId, scheduledFor),
        OrganizationId = "org-1",
        ProjectId = "project-1",
        RecurrenceId = recurrenceId,
        TemplateId = "template-1",
        ScheduledForUtc = scheduledFor,
        CreatedAt = new DateTimeOffset(2026, 7, 20, 9, 0, 0, TimeSpan.Zero)
    };
}
