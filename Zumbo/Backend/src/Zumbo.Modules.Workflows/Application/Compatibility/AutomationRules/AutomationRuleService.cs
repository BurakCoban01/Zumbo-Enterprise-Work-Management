using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed class AutomationRuleService(
    IDocumentRepository<AutomationRuleDocument> rules,
    IAutomationProjectAccessChecker access,
    IDistributedLockProvider distributedLockProvider,
    IOptions<DistributedLockOptions> distributedLockOptions,
    IClock clock,
    IAutomationAuditWriter audit,
    IExpectedVersionAccessor? expectedVersions = null)
{
    private readonly ExpectedVersionState expectedVersion = new(expectedVersions);

    public async Task<AutomationRuleResponse> SaveDraftAsync(
        string? ruleId,
        DefineAutomationRuleRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var definition = AutomationRuleDefinitionFactory.Define(request);
        var scope = await access.EnsureCanManageAsync(definition.ProjectId, ct);
        await using var resourceLock = await AcquireAsync(
            ruleId is null ? $"automation-project:{definition.ProjectId}" : $"automation-rule:{ruleId}",
            ct);
        var now = clock.UtcNow;

        AutomationRuleDocument rule;
        if (string.IsNullOrWhiteSpace(ruleId))
        {
            rule = new AutomationRuleDocument
            {
                OrganizationId = scope.OrganizationId,
                ProjectId = definition.ProjectId,
                CreatedByUserId = scope.ActorUserId,
                CreatedAt = now,
                UpdatedAt = now
            };
            rule.Draft = ToVersion(definition, 1, now);
            rule = await rules.CreateAsync(rule, ct);
        }
        else
        {
            rule = await GetRuleAsync(ruleId, includeArchived: false, ct);
            EnsureProject(rule, definition.ProjectId);
            EnsureTenant(rule, scope.OrganizationId);
            rule.Draft = ToVersion(definition, rule.PublishedVersion + 1, now);
            rule.UpdatedAt = now;
            await ReplaceAsync(rule, ct);
        }

        await audit.WriteAsync(
            "AutomationDraftSaved",
            rule.Id,
            rule.ProjectId,
            null,
            $"draft-v{rule.Draft!.Number}",
            correlationId,
            ct);
        return ToResponse(rule, rule.Draft);
    }

    public async Task<AutomationRuleResponse> PublishAsync(
        string ruleId,
        string correlationId,
        CancellationToken ct)
    {
        await using var resourceLock = await AcquireAsync($"automation-rule:{ruleId}", ct);
        var rule = await GetRuleAsync(ruleId, includeArchived: false, ct);
        var scope = await access.EnsureCanManageAsync(rule.ProjectId, ct);
        EnsureTenant(rule, scope.OrganizationId);
        var draft = rule.Draft
            ?? throw new ConflictException("AUTOMATION_DRAFT_REQUIRED", "Create an automation draft before publishing.");
        var publishedAt = clock.UtcNow;
        var published = CopyPublished(draft, publishedAt);
        rule.PublishedVersion = published.Number;
        rule.PublishedVersions.Add(published);
        rule.PublishedVersions = rule.PublishedVersions
            .OrderBy(version => version.Number)
            .ToList();
        rule.Draft = null;
        rule.Active = true;
        rule.NextRunAtUtc = NextRun(published.Trigger, publishedAt);
        ClearScheduleClaim(rule);
        rule.UpdatedAt = publishedAt;
        await ReplaceAsync(rule, ct);
        await audit.WriteAsync(
            "AutomationPublished",
            rule.Id,
            rule.ProjectId,
            "draft",
            $"published-v{published.Number}",
            correlationId,
            ct);
        return ToResponse(rule, published);
    }

    public async Task<AutomationRuleResponse> SetActiveAsync(
        string ruleId,
        bool active,
        string correlationId,
        CancellationToken ct)
    {
        await using var resourceLock = await AcquireAsync($"automation-rule:{ruleId}", ct);
        var rule = await GetRuleAsync(ruleId, includeArchived: false, ct);
        var scope = await access.EnsureCanManageAsync(rule.ProjectId, ct);
        EnsureTenant(rule, scope.OrganizationId);
        if (rule.PublishedVersion == 0)
            throw new ConflictException("AUTOMATION_PUBLISHED_REQUIRED", "Publish automation before activating it.");
        if (rule.Active == active)
            throw new ConflictException("AUTOMATION_STATE_UNCHANGED", "Automation state is unchanged.");
        var oldValue = rule.Active ? "Active" : "Paused";
        rule.Active = active;
        rule.NextRunAtUtc = active
            ? NextRun(CurrentPublished(rule).Trigger, clock.UtcNow)
            : null;
        ClearScheduleClaim(rule);
        rule.UpdatedAt = clock.UtcNow;
        await ReplaceAsync(rule, ct);
        await audit.WriteAsync(
            "AutomationStateChanged",
            rule.Id,
            rule.ProjectId,
            oldValue,
            active ? "Active" : "Paused",
            correlationId,
            ct);
        return ToResponse(rule, CurrentPublished(rule));
    }

    public async Task ArchiveAsync(string ruleId, string correlationId, CancellationToken ct)
    {
        await using var resourceLock = await AcquireAsync($"automation-rule:{ruleId}", ct);
        var rule = await GetRuleAsync(ruleId, includeArchived: false, ct);
        var scope = await access.EnsureCanManageAsync(rule.ProjectId, ct);
        EnsureTenant(rule, scope.OrganizationId);
        var oldValue = rule.Active ? "Active" : "Paused";
        rule.Active = false;
        rule.Archived = true;
        rule.NextRunAtUtc = null;
        ClearScheduleClaim(rule);
        rule.UpdatedAt = clock.UtcNow;
        await ReplaceAsync(rule, ct);
        await audit.WriteAsync(
            "AutomationArchived",
            rule.Id,
            rule.ProjectId,
            oldValue,
            "Archived",
            correlationId,
            ct);
    }

    public async Task<AutomationRuleResponse> GetAsync(
        string ruleId,
        bool draft,
        CancellationToken ct)
    {
        var rule = await GetRuleAsync(ruleId, includeArchived: true, ct);
        var scope = await access.EnsureCanViewAsync(rule.ProjectId, ct);
        EnsureTenant(rule, scope.OrganizationId);
        var version = draft
            ? rule.Draft ?? throw new NotFoundException(
                "AUTOMATION_DRAFT_NOT_FOUND",
                "Automation draft was not found.")
            : rule.PublishedVersion == 0 ? rule.Draft : CurrentPublished(rule);
        return ToResponse(rule, version);
    }

    public async Task<AutomationRulePageResponse> ListAsync(
        string projectId,
        bool includeArchived,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var scope = await access.EnsureCanViewAsync(projectId, ct);
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var filter = (System.Linq.Expressions.Expression<Func<AutomationRuleDocument, bool>>)(rule =>
            rule.ProjectId == projectId
            && rule.OrganizationId == scope.OrganizationId
            && (includeArchived || !rule.Archived));
        var total = await rules.CountByFilterAsync(filter, ct);
        var documents = await rules.ListByFilterAsync(
            filter,
            rule => rule.CreatedAt,
            orderDescending: false,
            page,
            pageSize,
            cancellationToken: ct);
        return new AutomationRulePageResponse(
            documents.Select(ToSummary).ToArray(),
            page,
            pageSize,
            total);
    }

    public async Task<AutomationDryRunResponse> DryRunAsync(
        string ruleId,
        AutomationDryRunContext context,
        CancellationToken ct)
    {
        var rule = await GetRuleAsync(ruleId, includeArchived: false, ct);
        var scope = await access.EnsureCanManageAsync(rule.ProjectId, ct);
        EnsureTenant(rule, scope.OrganizationId);
        ValidateDryRunContext(context);
        var version = rule.Draft ?? (rule.PublishedVersion > 0 ? CurrentPublished(rule) : null)
            ?? throw new ConflictException("AUTOMATION_DEFINITION_REQUIRED", "Automation definition is required.");
        var triggerMatched = TriggerMatches(version.Trigger, context);
        var conditionMatched = triggerMatched
            && (version.Condition is null || Evaluate(version.Condition, context.Fields));
        var actions = conditionMatched
            ? version.Actions.Select(ToDefinition).ToArray()
            : [];
        return new AutomationDryRunResponse(
            rule.Id,
            version.Number,
            triggerMatched,
            conditionMatched,
            actions,
            !triggerMatched ? "TriggerMismatch" : conditionMatched ? "WouldExecute" : "ConditionMismatch");
    }

    private async Task<AutomationRuleDocument> GetRuleAsync(
        string ruleId,
        bool includeArchived,
        CancellationToken ct) =>
        await rules.SelectAsync(
            rule => rule.Id == ruleId && (includeArchived || !rule.Archived),
            ct)
        ?? throw new NotFoundException("AUTOMATION_RULE_NOT_FOUND", "Automation rule was not found.");

    private async Task ReplaceAsync(AutomationRuleDocument rule, CancellationToken ct)
    {
        var result = await rules.ReplaceByVersionAsync(
            candidate => candidate.Id == rule.Id,
            rule,
            expectedVersion.Consume(rule.Version),
            ct);
        if (!result.Found)
            throw new NotFoundException("AUTOMATION_RULE_NOT_FOUND", "Automation rule was not found.");
        rule.Version = result.Version!.Value;
    }

    private async Task<IAsyncDisposable> AcquireAsync(string resource, CancellationToken ct)
    {
        var options = distributedLockOptions.Value;
        return await distributedLockProvider.TryAcquireAsync(
            resource,
            TimeSpan.FromSeconds(Math.Clamp(options.LeaseSeconds, 5, 300)),
            TimeSpan.FromSeconds(Math.Clamp(options.WaitSeconds, 0, 30)),
            ct)
            ?? throw new ConflictException("AUTOMATION_RESOURCE_BUSY", "Automation is busy; retry the operation.");
    }

    private static AutomationRuleVersionDocument ToVersion(
        AutomationRuleDefinition definition,
        int number,
        DateTimeOffset createdAt) =>
        new()
        {
            Number = number,
            State = "Draft",
            Name = definition.Name,
            Description = definition.Description,
            Trigger = ToDocument(definition.Trigger),
            Condition = definition.Condition is null ? null : ToDocument(definition.Condition),
            Actions = definition.Actions.Select(ToDocument).ToList(),
            MaximumExecutionsPerHour = definition.MaximumExecutionsPerHour,
            MaximumChainDepth = definition.MaximumChainDepth,
            CreatedAt = createdAt
        };

    private static AutomationRuleVersionDocument CopyPublished(
        AutomationRuleVersionDocument draft,
        DateTimeOffset publishedAt) =>
        new()
        {
            Number = draft.Number,
            State = "Published",
            Name = draft.Name,
            Description = draft.Description,
            Trigger = Copy(draft.Trigger),
            Condition = draft.Condition is null ? null : Copy(draft.Condition),
            Actions = draft.Actions.Select(Copy).ToList(),
            MaximumExecutionsPerHour = draft.MaximumExecutionsPerHour,
            MaximumChainDepth = draft.MaximumChainDepth,
            CreatedAt = draft.CreatedAt,
            PublishedAt = publishedAt
        };

    private static AutomationRuleVersionDocument CurrentPublished(AutomationRuleDocument rule) =>
        rule.PublishedVersions.Single(version => version.Number == rule.PublishedVersion);

    private static DateTimeOffset? NextRun(AutomationTriggerDocument trigger, DateTimeOffset now)
    {
        if (trigger.Type != "Schedule")
            return null;
        var start = trigger.StartAtUtc?.ToUniversalTime() ?? now;
        if (start > now)
            return start;
        return now.AddMinutes(trigger.IntervalMinutes!.Value);
    }

    private static void ClearScheduleClaim(AutomationRuleDocument rule)
    {
        rule.ScheduleClaimedForUtc = null;
        rule.ScheduleClaimedUntilUtc = null;
        rule.ScheduleClaimToken = null;
    }

    private static bool TriggerMatches(AutomationTriggerDocument trigger, AutomationDryRunContext context)
    {
        if (!trigger.Type.Equals(context.TriggerType, StringComparison.OrdinalIgnoreCase))
            return false;
        return trigger.Type == "Schedule"
            || trigger.EventType!.Equals(context.EventType, StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateDryRunContext(AutomationDryRunContext? context)
    {
        if (context is null || context.Fields is null)
            throw new ValidationException("Automation dry-run context and fields are required.");
        if (context.Fields.Count > AutomationRuleDefinitionFactory.MaximumConditionNodes)
            throw new ValidationException(
                $"Automation dry-run cannot contain more than {AutomationRuleDefinitionFactory.MaximumConditionNodes} fields.");
        if (context.SourceId?.Length > 128)
            throw new ValidationException("Automation dry-run source id cannot exceed 128 characters.");
        if (context.Fields.Any(field =>
                string.IsNullOrWhiteSpace(field.Key)
                || field.Key.Length > 50
                || field.Value?.Length > 2000))
            throw new ValidationException(
                "Automation dry-run field keys and values exceed the supported bounds.");
    }

    private static bool Evaluate(
        AutomationConditionDocument condition,
        IReadOnlyDictionary<string, string?> fields)
    {
        if (condition.Kind == "All")
            return condition.Children.All(child => Evaluate(child, fields));
        if (condition.Kind == "Any")
            return condition.Children.Any(child => Evaluate(child, fields));

        var actual = fields.FirstOrDefault(pair =>
            pair.Key.Equals(condition.Field, StringComparison.OrdinalIgnoreCase)).Value;
        return condition.Operator switch
        {
            "Equals" => string.Equals(actual, condition.Value, StringComparison.OrdinalIgnoreCase),
            "NotEquals" => !string.Equals(actual, condition.Value, StringComparison.OrdinalIgnoreCase),
            "Contains" => actual?.Contains(condition.Value!, StringComparison.OrdinalIgnoreCase) == true,
            "NotContains" => actual?.Contains(condition.Value!, StringComparison.OrdinalIgnoreCase) != true,
            "IsEmpty" => string.IsNullOrWhiteSpace(actual),
            "IsNotEmpty" => !string.IsNullOrWhiteSpace(actual),
            _ => false
        };
    }

    private static AutomationRuleSummaryResponse ToSummary(AutomationRuleDocument rule)
    {
        var version = rule.Draft ?? (rule.PublishedVersion > 0 ? CurrentPublished(rule) : null)
            ?? throw new InvalidOperationException("Stored automation rule has no definition.");
        return new AutomationRuleSummaryResponse(
            rule.Id,
            rule.ProjectId,
            version.Name,
            version.Trigger.Type,
            version.Trigger.EventType,
            rule.Active,
            rule.Archived,
            rule.NextRunAtUtc,
            rule.PublishedVersion,
            rule.Draft is not null,
            rule.Version);
    }

    private static AutomationRuleResponse ToResponse(
        AutomationRuleDocument rule,
        AutomationRuleVersionDocument? version) =>
        new(
            rule.Id,
            rule.ProjectId,
            rule.Active,
            rule.Archived,
            rule.NextRunAtUtc,
            rule.PublishedVersion,
            rule.Draft is not null,
            rule.Version,
            version is null ? null : ToResponse(version));

    private static AutomationRuleVersionResponse ToResponse(AutomationRuleVersionDocument version) =>
        new(
            version.Number,
            version.State,
            version.Name,
            version.Description,
            ToDefinition(version.Trigger),
            version.Condition is null ? null : ToDefinition(version.Condition),
            version.Actions.Select(ToDefinition).ToArray(),
            version.MaximumExecutionsPerHour,
            version.MaximumChainDepth,
            version.CreatedAt,
            version.PublishedAt);

    private static AutomationTriggerDocument ToDocument(AutomationTriggerDefinition trigger) =>
        new()
        {
            Type = trigger.Type,
            EventType = trigger.EventType,
            IntervalMinutes = trigger.IntervalMinutes,
            StartAtUtc = trigger.StartAtUtc
        };

    private static AutomationConditionDocument ToDocument(AutomationConditionDefinition condition) =>
        new()
        {
            Kind = condition.Kind,
            Field = condition.Field,
            Operator = condition.Operator,
            Value = condition.Value,
            Children = condition.Children.Select(ToDocument).ToList()
        };

    private static AutomationActionDocument ToDocument(AutomationActionDefinition action) =>
        new() { Type = action.Type, Value = action.Value };

    private static AutomationTriggerDocument Copy(AutomationTriggerDocument trigger) =>
        new()
        {
            Type = trigger.Type,
            EventType = trigger.EventType,
            IntervalMinutes = trigger.IntervalMinutes,
            StartAtUtc = trigger.StartAtUtc
        };

    private static AutomationConditionDocument Copy(AutomationConditionDocument condition) =>
        new()
        {
            Kind = condition.Kind,
            Field = condition.Field,
            Operator = condition.Operator,
            Value = condition.Value,
            Children = condition.Children.Select(Copy).ToList()
        };

    private static AutomationActionDocument Copy(AutomationActionDocument action) =>
        new() { Type = action.Type, Value = action.Value };

    private static AutomationTriggerDefinition ToDefinition(AutomationTriggerDocument trigger) =>
        new(trigger.Type, trigger.EventType, trigger.IntervalMinutes, trigger.StartAtUtc);

    private static AutomationConditionDefinition ToDefinition(AutomationConditionDocument condition) =>
        new(
            condition.Kind,
            condition.Field,
            condition.Operator,
            condition.Value,
            condition.Children.Select(ToDefinition).ToArray());

    private static AutomationActionDefinition ToDefinition(AutomationActionDocument action) =>
        new(action.Type, action.Value);

    private static void EnsureProject(AutomationRuleDocument rule, string projectId)
    {
        if (!rule.ProjectId.Equals(projectId, StringComparison.Ordinal))
            throw new ConflictException(
                "AUTOMATION_PROJECT_IMMUTABLE",
                "Automation rule cannot move between projects.");
    }

    private static void EnsureTenant(AutomationRuleDocument rule, string organizationId)
    {
        if (!rule.OrganizationId.Equals(organizationId, StringComparison.Ordinal))
            throw new ForbiddenException("Automation rule does not belong to the current tenant.");
    }
}
