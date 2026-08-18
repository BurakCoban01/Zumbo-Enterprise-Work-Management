using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Infrastructure.Concurrency;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.WorkItems;
using Zumbo.Modules.WorkItems.Application.Features.Recurrences;
using Zumbo.SharedKernel;

namespace Zumbo.UnitTests;

public sealed class ScheduleDueRecurrencesIdempotencyTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);

    private readonly InMemoryDocumentRepository<WorkItemTemplateDocument> templates = new();
    private readonly InMemoryDocumentRepository<WorkItemRecurrenceDocument> recurrences = new();
    private readonly InMemoryDocumentRepository<WorkItemRecurrenceOccurrenceDocument> occurrences = new();
    private readonly RecordingPublisher publisher = new();

    [Fact]
    public async Task PreExistingOccurrenceWithLegacyId_IsReused_NotDuplicated()
    {
        var (template, recurrence, scheduledFor) = await SeedAsync();
        var legacyId = "legacy-non-deterministic-" + Guid.NewGuid().ToString("N");
        await occurrences.CreateAsync(new WorkItemRecurrenceOccurrenceDocument
        {
            Id = legacyId,
            OrganizationId = recurrence.OrganizationId,
            ProjectId = recurrence.ProjectId,
            RecurrenceId = recurrence.Id,
            TemplateId = template.Id,
            ScheduledForUtc = scheduledFor,
            CreatedAt = Now
        });

        var handler = CreateHandler();
        var scheduled = await handler.HandleAsync(new ScheduleDueRecurrencesCommand(), default);

        Assert.Equal(1, scheduled);
        var all = await occurrences.ListByFilterAsync(
            x => x.RecurrenceId == recurrence.Id,
            x => x.ScheduledForUtc);
        var stored = Assert.Single(all);
        Assert.Equal(legacyId, stored.Id);
        var published = Assert.Single(publisher.Events);
        Assert.Equal(legacyId, published.OccurrenceId);
        Assert.Equal(scheduledFor, published.ScheduledForUtc);

        var advanced = await recurrences.SelectAsync(x => x.Id == recurrence.Id);
        Assert.NotNull(advanced);
        Assert.Equal(1, advanced.ScheduledOccurrences);
        Assert.Equal(scheduledFor.AddDays(7), advanced.NextRunAtUtc);
        Assert.True(advanced.Active);
    }

    [Fact]
    public async Task PreExistingOccurrenceWithDeterministicId_IsReused_NotDuplicated()
    {
        var (template, recurrence, scheduledFor) = await SeedAsync();
        var deterministicId = WorkItemTemplateRecurrenceService.StableOccurrenceId(recurrence.Id, scheduledFor);
        await occurrences.CreateAsync(new WorkItemRecurrenceOccurrenceDocument
        {
            Id = deterministicId,
            OrganizationId = recurrence.OrganizationId,
            ProjectId = recurrence.ProjectId,
            RecurrenceId = recurrence.Id,
            TemplateId = template.Id,
            ScheduledForUtc = scheduledFor,
            CreatedAt = Now
        });

        var handler = CreateHandler();
        var scheduled = await handler.HandleAsync(new ScheduleDueRecurrencesCommand(), default);

        Assert.Equal(1, scheduled);
        var all = await occurrences.ListByFilterAsync(
            x => x.RecurrenceId == recurrence.Id,
            x => x.ScheduledForUtc);
        var stored = Assert.Single(all);
        Assert.Equal(deterministicId, stored.Id);
        var published = Assert.Single(publisher.Events);
        Assert.Equal(deterministicId, published.OccurrenceId);
    }

    [Fact]
    public async Task NoExistingOccurrence_CreatesDeterministicOccurrence()
    {
        var (_, recurrence, scheduledFor) = await SeedAsync();

        var handler = CreateHandler();
        var scheduled = await handler.HandleAsync(new ScheduleDueRecurrencesCommand(), default);

        Assert.Equal(1, scheduled);
        var all = await occurrences.ListByFilterAsync(
            x => x.RecurrenceId == recurrence.Id,
            x => x.ScheduledForUtc);
        var stored = Assert.Single(all);
        Assert.Equal(
            WorkItemTemplateRecurrenceService.StableOccurrenceId(recurrence.Id, scheduledFor),
            stored.Id);
        Assert.Equal(scheduledFor, stored.ScheduledForUtc);
        var published = Assert.Single(publisher.Events);
        Assert.Equal(stored.Id, published.OccurrenceId);
    }

    [Fact]
    public async Task SecondCycleWithNoDueWork_DoesNothing()
    {
        var (_, recurrence, _) = await SeedAsync(maxOccurrences: 1);
        var handler = CreateHandler();
        await handler.HandleAsync(new ScheduleDueRecurrencesCommand(), default);

        var scheduled = await handler.HandleAsync(new ScheduleDueRecurrencesCommand(), default);

        Assert.Equal(0, scheduled);
        Assert.Single(await occurrences.ListByFilterAsync(
            x => x.RecurrenceId == recurrence.Id,
            x => x.ScheduledForUtc));
        Assert.Single(publisher.Events);
    }

    private async Task<(WorkItemTemplateDocument Template, WorkItemRecurrenceDocument Recurrence, DateTimeOffset ScheduledFor)> SeedAsync(int maxOccurrences = 12)
    {
        var template = await templates.CreateAsync(new WorkItemTemplateDocument
        {
            Id = "template-1",
            OrganizationId = "org-1",
            ProjectId = "project-1",
            BoardId = "board-1",
            Name = "Review",
            Title = "Review",
            CreatedAt = Now,
            UpdatedAt = Now
        });
        var scheduledFor = new DateTimeOffset(2026, 7, 23, 13, 46, 0, TimeSpan.Zero);
        var recurrence = await recurrences.CreateAsync(new WorkItemRecurrenceDocument
        {
            Id = "recurrence-1",
            OrganizationId = "org-1",
            ProjectId = "project-1",
            TemplateId = template.Id,
            Frequency = WorkItemRecurrenceFrequencies.Weekly,
            Interval = 1,
            StartAtUtc = scheduledFor,
            NextRunAtUtc = scheduledFor,
            MaxOccurrences = maxOccurrences,
            CreatedByUserId = "user-1",
            CreatedAt = Now,
            UpdatedAt = Now
        });
        return (template, recurrence, scheduledFor);
    }

    private ScheduleDueRecurrencesHandler CreateHandler() => new(
        templates,
        recurrences,
        occurrences,
        publisher,
        new InMemoryDistributedLockProvider(),
        Options.Create(new DistributedLockOptions()),
        Options.Create(new WorkItemRecurrenceOptions()),
        new FixedClock());

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class RecordingPublisher : IWorkItemRecurrenceEventPublisher
    {
        public List<WorkItemRecurrenceDueEvent> Events { get; } = [];

        public Task PublishAsync(WorkItemRecurrenceDueEvent message, CancellationToken ct)
        {
            Events.Add(message);
            return Task.CompletedTask;
        }
    }
}
