using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Workflows;

namespace Zumbo.RepositoryContracts;

public abstract class AutomationRepositoryContract
{
    protected abstract IDocumentRepository<AutomationRuleDocument> Rules();

    [Fact]
    public async Task RuleStore_PreservesTenantNestedDefinitionAndCompareExchange()
    {
        var rules = Rules();
        var prefix = "feature002-contract-" + Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        var rule = new AutomationRuleDocument
        {
            Id = prefix + "-rule",
            OrganizationId = prefix + "-organization",
            ProjectId = prefix + "-project",
            Draft = new AutomationRuleVersionDocument
            {
                Number = 1,
                State = "Draft",
                Name = "Provider contract",
                Trigger = new AutomationTriggerDocument
                {
                    Type = "Event",
                    EventType = "WorkItemTransitioned"
                },
                Condition = new AutomationConditionDocument
                {
                    Kind = "All",
                    Children =
                    [
                        new AutomationConditionDocument
                        {
                            Kind = "Field",
                            Field = "Priority",
                            Operator = "Equals",
                            Value = "High"
                        },
                        new AutomationConditionDocument
                        {
                            Kind = "Any",
                            Children =
                            [
                                new AutomationConditionDocument
                                {
                                    Kind = "Field",
                                    Field = "Labels",
                                    Operator = "Contains",
                                    Value = "triage"
                                }
                            ]
                        }
                    ]
                },
                Actions =
                [
                    new AutomationActionDocument { Type = "AddLabel", Value = "automated" },
                    new AutomationActionDocument { Type = "SetPriority", Value = "Critical" }
                ],
                MaximumExecutionsPerHour = 40,
                MaximumChainDepth = 3,
                CreatedAt = now
            },
            CreatedByUserId = prefix + "-user",
            CreatedAt = now,
            UpdatedAt = now
        };

        try
        {
            rule = await rules.CreateAsync(rule);
            var stale = await rules.SelectAsync(candidate => candidate.Id == rule.Id);
            var loaded = await rules.SelectAsync(candidate =>
                candidate.Id == rule.Id
                && candidate.OrganizationId == rule.OrganizationId
                && candidate.ProjectId == rule.ProjectId);
            Assert.NotNull(loaded);
            Assert.Equal("WorkItemTransitioned", loaded.Draft!.Trigger.EventType);
            Assert.Equal("Any", loaded.Draft.Condition!.Children.ElementAt(1).Kind);
            Assert.Equal("triage", loaded.Draft.Condition.Children.ElementAt(1).Children.Single().Value);
            Assert.Equal(["AddLabel", "SetPriority"], loaded.Draft.Actions.Select(action => action.Type));

            rule.Active = true;
            rule.NextRunAtUtc = now;
            rule.UpdatedAt = now.AddMinutes(1);
            var replaced = await rules.ReplaceByVersionAsync(
                candidate => candidate.Id == rule.Id
                    && candidate.OrganizationId == rule.OrganizationId,
                rule,
                rule.Version);
            Assert.True(replaced.Found);
            rule.Version = replaced.Version!.Value;
            var due = await rules.ListByFilterAsync(
                candidate => candidate.Active
                    && candidate.NextRunAtUtc <= now.AddMinutes(1)
                    && (candidate.ScheduleClaimedUntilUtc == null
                        || candidate.ScheduleClaimedUntilUtc <= now),
                candidate => candidate.NextRunAtUtc!,
                pageSize: 20);
            Assert.Equal(rule.Id, Assert.Single(due).Id);

            rule.ScheduleClaimedForUtc = now;
            rule.ScheduleClaimedUntilUtc = now.AddMinutes(5);
            rule.ScheduleClaimToken = prefix + "-claim";
            var claimed = await rules.ReplaceByVersionAsync(
                candidate => candidate.Id == rule.Id,
                rule,
                rule.Version);
            Assert.True(claimed.Found);
            Assert.Empty(await rules.ListByFilterAsync(
                candidate => candidate.Active
                    && candidate.NextRunAtUtc <= now.AddMinutes(1)
                    && (candidate.ScheduleClaimedUntilUtc == null
                        || candidate.ScheduleClaimedUntilUtc <= now),
                candidate => candidate.NextRunAtUtc!,
                pageSize: 20));

            stale!.Archived = true;
            await Assert.ThrowsAsync<DocumentConcurrencyException>(() =>
                rules.ReplaceByVersionAsync(
                    candidate => candidate.Id == stale.Id,
                    stale,
                    stale.Version));

            Assert.Null(await rules.SelectAsync(candidate =>
                candidate.Id == rule.Id
                && candidate.OrganizationId == prefix + "-foreign"));
            var listed = await rules.ListByFilterAsync(
                candidate => candidate.OrganizationId == rule.OrganizationId
                    && candidate.ProjectId == rule.ProjectId
                    && !candidate.Archived,
                candidate => candidate.CreatedAt,
                pageSize: 20);
            Assert.Equal(rule.Id, Assert.Single(listed).Id);
        }
        finally
        {
            await rules.DeleteByFilterAsync(candidate => candidate.Id == rule.Id);
        }
    }
}
