using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Workflows;

namespace Zumbo.RepositoryContracts;

public abstract class AutomationRunRepositoryContract
{
    protected abstract IDocumentRepository<AutomationRunDocument> Runs();

    [Fact]
    public async Task RunStore_PreservesIdempotencyTenantStepsAndRetryQuery()
    {
        var runs = Runs();
        var prefix = "feature002-run-" + Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        var run = new AutomationRunDocument
        {
            Id = prefix + "-stable",
            OrganizationId = prefix + "-organization",
            ProjectId = prefix + "-project",
            RuleId = prefix + "-rule",
            RuleVersion = 2,
            RuleName = "Provider run contract",
            TriggerType = "Event",
            EventType = "WorkItemUpdated",
            TriggerId = prefix + "-trigger",
            SourceId = prefix + "-work",
            ActorUserId = prefix + "-user",
            RootRunId = prefix + "-root",
            ChainDepth = 1,
            VisitedRuleIds = [prefix + "-parent-rule"],
            Fields = new Dictionary<string, string?>
            {
                ["Priority"] = "High",
                ["Labels"] = "triage,customer"
            },
            Status = AutomationRunStates.RetryScheduled,
            Outcome = AutomationRunStates.RetryScheduled,
            Attempt = 1,
            Steps =
            [
                new AutomationRunStepDocument
                {
                    Index = 0,
                    ActionType = "AddLabel",
                    Status = AutomationStepStates.Failed,
                    Attempt = 1,
                    FailureCategory = "TransientDependency"
                }
            ],
            CorrelationId = prefix + "-correlation",
            CreatedAtUtc = now,
            NextAttemptAtUtc = now.AddMinutes(-1)
        };

        try
        {
            run = await runs.CreateAsync(run);
            await Assert.ThrowsAsync<DocumentConflictException>(() =>
                runs.CreateAsync(run));

            var loaded = await runs.SelectAsync(candidate =>
                candidate.Id == run.Id
                && candidate.OrganizationId == run.OrganizationId
                && candidate.ProjectId == run.ProjectId);
            Assert.NotNull(loaded);
            Assert.Equal("High", loaded.Fields["Priority"]);
            Assert.Equal("AddLabel", Assert.Single(loaded.Steps).ActionType);
            Assert.Equal(prefix + "-parent-rule", Assert.Single(loaded.VisitedRuleIds));

            var stale = await runs.SelectAsync(candidate => candidate.Id == run.Id);
            run.Status = AutomationRunStates.Succeeded;
            run.Outcome = AutomationRunStates.Succeeded;
            run.NextAttemptAtUtc = null;
            var replaced = await runs.ReplaceByVersionAsync(
                candidate => candidate.Id == run.Id,
                run,
                run.Version);
            Assert.True(replaced.Found);
            stale!.Status = AutomationRunStates.DeadLetter;
            await Assert.ThrowsAsync<DocumentConcurrencyException>(() =>
                runs.ReplaceByVersionAsync(
                    candidate => candidate.Id == stale.Id,
                    stale,
                    stale.Version));

            Assert.Null(await runs.SelectAsync(candidate =>
                candidate.Id == run.Id
                && candidate.OrganizationId == prefix + "-foreign"));
            var completed = await runs.ListByFilterAsync(
                candidate => candidate.OrganizationId == run.OrganizationId
                    && candidate.ProjectId == run.ProjectId
                    && candidate.Status == AutomationRunStates.Succeeded,
                candidate => candidate.CreatedAtUtc,
                orderDescending: true,
                pageSize: 20);
            Assert.Equal(run.Id, Assert.Single(completed).Id);
        }
        finally
        {
            await runs.DeleteByFilterAsync(candidate => candidate.Id == run.Id);
        }
    }
}
