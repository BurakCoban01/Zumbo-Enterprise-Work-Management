using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.WorkItems;

namespace Zumbo.RepositoryContracts;

public abstract class WorkItemCollaborationRepositoryContract
{
    protected abstract IDocumentRepository<WorkItemCollaborationDocument> Collaborations();
    protected abstract IDocumentRepository<WorkItemEventActivityDocument> Activities();
    protected abstract IDocumentRepository<WorkItemTemplateDocument> Templates();
    protected abstract IDocumentRepository<WorkItemRecurrenceDocument> Recurrences();
    protected abstract IDocumentRepository<WorkItemRecurrenceOccurrenceDocument> Occurrences();

    [Fact]
    public async Task CollaborationAndRecurrenceStores_PreserveOwnershipCasAndBoundedQueries()
    {
        var repositories = Repositories();
        var prefix = "domain009-contract-" + Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        var collaboration = new WorkItemCollaborationDocument
        {
            Id = prefix + "-item",
            OrganizationId = prefix + "-org",
            ProjectId = prefix + "-project",
            WorkItemId = prefix + "-item",
            WatcherUserIds = ["user-a"],
            VoterUserIds = ["user-b"],
            UpdatedAt = now
        };
        var template = new WorkItemTemplateDocument
        {
            Id = prefix + "-template",
            OrganizationId = prefix + "-org",
            ProjectId = prefix + "-project",
            BoardId = prefix + "-board",
            Name = "Daily review",
            Title = "Review",
            CreatedByUserId = "user-a",
            CreatedAt = now,
            UpdatedAt = now
        };
        var recurrence = new WorkItemRecurrenceDocument
        {
            Id = prefix + "-recurrence",
            OrganizationId = prefix + "-org",
            ProjectId = prefix + "-project",
            TemplateId = template.Id,
            Frequency = WorkItemRecurrenceFrequencies.Daily,
            StartAtUtc = now,
            NextRunAtUtc = now,
            MaxOccurrences = 2,
            CreatedByUserId = "user-a",
            CreatedAt = now,
            UpdatedAt = now
        };
        var occurrence = new WorkItemRecurrenceOccurrenceDocument
        {
            Id = WorkItemTemplateRecurrenceService.StableOccurrenceId(recurrence.Id, now),
            OrganizationId = recurrence.OrganizationId,
            ProjectId = recurrence.ProjectId,
            RecurrenceId = recurrence.Id,
            TemplateId = template.Id,
            ScheduledForUtc = now,
            CreatedAt = now
        };

        try
        {
            collaboration = await repositories.Collaborations.CreateAsync(collaboration);
            await repositories.Activities.CreateAsync(new WorkItemEventActivityDocument
            {
                Id = prefix + "-activity",
                OrganizationId = collaboration.OrganizationId,
                ProjectId = collaboration.ProjectId,
                WorkItemId = collaboration.WorkItemId,
                Type = "WorkItemWatched",
                ActorUserId = "user-a",
                Detail = "Watch enabled",
                CreatedAt = now
            });
            template = await repositories.Templates.CreateAsync(template);
            recurrence = await repositories.Recurrences.CreateAsync(recurrence);
            occurrence = await repositories.Occurrences.CreateAsync(occurrence);

            var first = await repositories.Collaborations.SelectAsync(x => x.Id == collaboration.Id);
            var stale = await repositories.Collaborations.SelectAsync(x => x.Id == collaboration.Id);
            first!.WatcherUserIds.Add("user-c");
            var updated = await repositories.Collaborations.ReplaceByVersionAsync(
                x => x.Id == first.Id, first, first.Version);
            Assert.True(updated.Found);
            stale!.WatcherUserIds.Add("user-d");
            await Assert.ThrowsAsync<DocumentConcurrencyException>(() =>
                repositories.Collaborations.ReplaceByVersionAsync(
                    x => x.Id == stale.Id, stale, stale.Version));

            Assert.Null(await repositories.Collaborations.SelectAsync(x =>
                x.Id == collaboration.Id && x.OrganizationId == prefix + "-foreign"));
            Assert.Empty(await repositories.Activities.ListByFilterAsync(
                x => x.OrganizationId == prefix + "-foreign",
                x => x.CreatedAt,
                pageSize: 10));

            var due = await repositories.Recurrences.ListByFilterAsync(
                x => x.Active && !x.Archived && x.NextRunAtUtc <= now.AddSeconds(1),
                x => x.NextRunAtUtc!,
                pageSize: 1);
            Assert.Equal(recurrence.Id, Assert.Single(due).Id);
            var page = await repositories.Occurrences.ListByFilterAsync(
                x => x.RecurrenceId == recurrence.Id,
                x => x.ScheduledForUtc,
                pageSize: 1);
            Assert.Equal(occurrence.Id, Assert.Single(page).Id);
            await Assert.ThrowsAsync<DocumentConflictException>(() =>
                repositories.Occurrences.CreateAsync(new WorkItemRecurrenceOccurrenceDocument
                {
                    Id = occurrence.Id,
                    OrganizationId = occurrence.OrganizationId,
                    ProjectId = occurrence.ProjectId,
                    RecurrenceId = occurrence.RecurrenceId,
                    TemplateId = occurrence.TemplateId,
                    ScheduledForUtc = occurrence.ScheduledForUtc,
                    CreatedAt = now
                }));
        }
        finally
        {
            await repositories.Collaborations.DeleteByFilterAsync(x => x.Id.StartsWith(prefix));
            await repositories.Activities.DeleteByFilterAsync(x => x.Id.StartsWith(prefix));
            await repositories.Templates.DeleteByFilterAsync(x => x.Id.StartsWith(prefix));
            await repositories.Recurrences.DeleteByFilterAsync(x => x.Id.StartsWith(prefix));
            await repositories.Occurrences.DeleteByFilterAsync(x => x.RecurrenceId.StartsWith(prefix));
        }
    }

    private RepositorySet Repositories() => new(
        Collaborations(), Activities(), Templates(), Recurrences(), Occurrences());

    private sealed record RepositorySet(
        IDocumentRepository<WorkItemCollaborationDocument> Collaborations,
        IDocumentRepository<WorkItemEventActivityDocument> Activities,
        IDocumentRepository<WorkItemTemplateDocument> Templates,
        IDocumentRepository<WorkItemRecurrenceDocument> Recurrences,
        IDocumentRepository<WorkItemRecurrenceOccurrenceDocument> Occurrences);
}
