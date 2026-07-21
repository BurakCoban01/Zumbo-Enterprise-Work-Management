using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.Audit;
using Zumbo.Modules.Boards;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.Projects;
using Zumbo.Modules.Teams;
using Zumbo.Modules.WorkItems;
using Zumbo.Modules.Workflows;
using Zumbo.SharedKernel;

public sealed class AuditAccessCheckerAdapter(
    IDocumentRepository<OrganizationDocument> organizations,
    IDocumentRepository<UserDocument> users,
    IDocumentRepository<ProjectDocument> projects,
    IDocumentRepository<TeamDocument> teams,
    IDocumentRepository<BoardDocument> boards,
    IDocumentRepository<WorkItemDocument> workItems,
    IDocumentRepository<WorkItemBulkJobDocument> workItemBulkJobs,
    IDocumentRepository<SprintDocument> sprints,
    IdentityPermissionService permissionService,
    ICurrentUser currentUser) : IAuditAccessChecker, IAuditTenantResolver
{
    public async Task<AuditReadScope> EnsureCanReadAsync(AuditLogQuery query, CancellationToken ct)
    {
        var userId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new UnauthorizedException("Authenticated user is required.");
        }

        var hasGlobalAuditAccess = await permissionService.HasPermissionAsync(PermissionCatalog.AuditReadAll, ct);

        if (hasGlobalAuditAccess)
        {
            var organizationId = query.OrganizationId;
            if (organizationId is null && query.EntityType is not null && query.EntityId is not null)
                organizationId = (await ResolveAsync(query.EntityType, query.EntityId, userId, ct)).OrganizationId;
            organizationId ??= currentUser.OrganizationId;
            if (string.IsNullOrWhiteSpace(organizationId))
                throw new ValidationException("Global audit queries require organization id or a tenant-scoped entity.");
            return new AuditReadScope(organizationId);
        }

        if (query.EntityType is not null && query.EntityId is not null)
        {
            if (query.EntityType.Equals("Organization", StringComparison.OrdinalIgnoreCase))
            {
                var organization = await organizations.SelectAsync(x => x.Id == query.EntityId, ct)
                    ?? throw new NotFoundException("ORGANIZATION_NOT_FOUND", "Organization was not found.");
                if (!string.Equals(organization.OwnerUserId, userId, StringComparison.Ordinal))
                {
                    throw new ForbiddenException("User cannot read audit records for this organization.");
                }

                return new AuditReadScope(organization.Id);
            }

            if (query.EntityType.Equals("Team", StringComparison.OrdinalIgnoreCase))
            {
                var team = await teams.SelectAsync(x => x.Id == query.EntityId, ct)
                    ?? throw new NotFoundException("TEAM_NOT_FOUND", "Team was not found.");
                if (team.Members.All(x => x.UserId != userId || x.Status != "Active"))
                {
                    throw new ForbiddenException("User cannot read audit records for this team.");
                }

                return new AuditReadScope(team.OrganizationId);
            }

            var projectId = await ResolveProjectIdAsync(query.EntityType, query.EntityId, ct);
            var project = await projects.SelectAsync(x => x.Id == projectId, ct)
                ?? throw new NotFoundException("PROJECT_NOT_FOUND", "Project was not found.");

            if (!hasGlobalAuditAccess && project.Members.All(x => x.UserId != userId))
            {
                throw new ForbiddenException("User cannot read audit records for this project.");
            }

            return new AuditReadScope(project.OrganizationId);
        }

        if (string.Equals(query.ActorUserId, userId, StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(currentUser.OrganizationId))
                throw new ForbiddenException("Current organization is required for an actor audit query.");
            if (query.OrganizationId is not null
                && !query.OrganizationId.Equals(currentUser.OrganizationId, StringComparison.Ordinal))
                throw new ForbiddenException("Audit query organization does not match the current tenant.");
            return new AuditReadScope(currentUser.OrganizationId);
        }

        throw new ForbiddenException("Audit queries must target the current user or an accessible project entity.");
    }

    public async Task<AuditTenant> ResolveAsync(
        string entityType,
        string entityId,
        string actorUserId,
        CancellationToken ct)
    {
        if (entityType.Equals("Organization", StringComparison.OrdinalIgnoreCase))
            return new AuditTenant(entityId, entityType, entityId);
        if (entityType.Equals("Team", StringComparison.OrdinalIgnoreCase))
        {
            var team = await teams.SelectAsync(x => x.Id == entityId, ct);
            if (team is not null) return new AuditTenant(team.OrganizationId, entityType, entityId);
        }
        if (entityType.Equals("Identity", StringComparison.OrdinalIgnoreCase))
        {
            var subject = await users.SelectAsync(x => x.Id == entityId, ct)
                ?? await users.SelectAsync(x => x.Id == actorUserId, ct);
            return new AuditTenant(subject?.OrganizationId ?? currentUser.OrganizationId ?? "system", entityType, entityId);
        }
        try
        {
            var projectId = await ResolveProjectIdAsync(entityType, entityId, ct);
            var project = await projects.SelectAsync(x => x.Id == projectId, ct);
            if (project is not null) return new AuditTenant(project.OrganizationId, entityType, entityId);
        }
        catch (NotFoundException) { }
        catch (ValidationException) { }
        return new AuditTenant(currentUser.OrganizationId ?? "system", entityType, entityId);
    }

    private async Task<string> ResolveProjectIdAsync(string entityType, string entityId, CancellationToken ct)
    {
        if (entityType.Equals("WorkItem", StringComparison.OrdinalIgnoreCase))
        {
            var workItem = await workItems.SelectAsync(x => x.Id == entityId, ct)
                ?? throw new NotFoundException("WORK_ITEM_NOT_FOUND", "Work item was not found.");
            return workItem.ProjectId;
        }

        if (entityType.Equals("WorkItemBulkJob", StringComparison.OrdinalIgnoreCase))
        {
            var job = await workItemBulkJobs.SelectAsync(x => x.Id == entityId, ct)
                ?? throw new NotFoundException("WORK_ITEM_BULK_JOB_NOT_FOUND", "Bulk job was not found.");
            return job.ProjectId;
        }

        if (entityType.Equals("Sprint", StringComparison.OrdinalIgnoreCase))
        {
            var sprint = await sprints.SelectAsync(x => x.Id == entityId, ct)
                ?? throw new NotFoundException("SPRINT_NOT_FOUND", "Sprint was not found.");
            return sprint.ProjectId;
        }

        if (entityType.Equals("Board", StringComparison.OrdinalIgnoreCase))
        {
            var board = await boards.SelectAsync(x => x.Id == entityId, ct)
                ?? throw new NotFoundException("BOARD_NOT_FOUND", "Board was not found.");
            return board.ProjectId;
        }

        if (entityType.Equals("Project", StringComparison.OrdinalIgnoreCase))
        {
            return entityId;
        }

        throw new ValidationException("Audit entity type must be WorkItem, WorkItemBulkJob, Sprint, Board, Project, Team or Organization.");
    }
}

public sealed class WorkflowPolicyAdapter(WorkflowService workflows) : IWorkflowPolicy
{
    public async Task<WorkflowTransitionRule> EnsureTransitionAllowedAsync(
        string projectId,
        string issueType,
        string fromStatus,
        string toStatus,
        CancellationToken ct)
    {
        var workflow = await workflows.GetOrCreateDefaultAsync(projectId, ct);
        var scheme = workflow.IssueTypeSchemes?.SingleOrDefault(x =>
                x.IssueType.Equals(issueType, StringComparison.OrdinalIgnoreCase))
            ?? workflow.IssueTypeSchemes?.SingleOrDefault(x => x.IssueType == "*")
            ?? throw new ConflictException("WORKFLOW_ISSUE_SCHEME_NOT_FOUND", $"No workflow scheme exists for issue type '{issueType}'.");
        if (!scheme.Statuses.Contains(fromStatus, StringComparer.OrdinalIgnoreCase)
            || !scheme.Statuses.Contains(toStatus, StringComparer.OrdinalIgnoreCase))
        {
            throw new ConflictException("WORKFLOW_ISSUE_SCHEME_TRANSITION_FORBIDDEN", "The issue type scheme does not allow this status transition.");
        }
        var transition = workflow.Transitions.SingleOrDefault(x =>
            x.FromStatus.Equals(fromStatus, StringComparison.OrdinalIgnoreCase)
            && x.ToStatus.Equals(toStatus, StringComparison.OrdinalIgnoreCase));

        if (transition is null)
        {
            throw new ConflictException("WORKFLOW_TRANSITION_FORBIDDEN", $"Transition from {fromStatus} to {toStatus} is not allowed.");
        }

        return new WorkflowTransitionRule(
            transition.FromStatus,
            transition.ToStatus,
            transition.RequiresAssignee,
            transition.RequiresCompletedChecklist,
            transition.RequiresApproval,
            transition.Automations.Select(x => new WorkflowAutomationRule(x.Action, x.Value)).ToList(),
            workflow.Statuses.Single(x =>
                x.Name.Equals(transition.ToStatus, StringComparison.OrdinalIgnoreCase)).Category);
    }
}

public sealed class WorkflowPublicationGuardAdapter(
    IDocumentRepository<BoardDocument> boards,
    IDocumentRepository<WorkItemDocument> workItems) : IWorkflowPublicationGuard
{
    public async Task ValidateAsync(WorkflowPublicationCandidate candidate, CancellationToken ct)
    {
        var statusNames = candidate.Statuses.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var activeBoards = await ReadAllAsync(boards, x => x.ProjectId == candidate.ProjectId && !x.Archived, ct);
        var activeItems = await ReadAllAsync(workItems, x => x.ProjectId == candidate.ProjectId && !x.Archived, ct);

        foreach (var item in activeItems)
        {
            if (!statusNames.Contains(item.Status))
            {
                throw new ConflictException("WORKFLOW_PUBLISH_EXISTING_STATUS_INVALID", "Publish would invalidate an existing work item status.");
            }
            var scheme = candidate.IssueTypeSchemes.SingleOrDefault(x =>
                    x.IssueType.Equals(item.Type, StringComparison.OrdinalIgnoreCase))
                ?? candidate.IssueTypeSchemes.SingleOrDefault(x => x.IssueType == "*")
                ?? throw new ConflictException("WORKFLOW_PUBLISH_ISSUE_SCHEME_MISSING", "Publish would leave an existing issue type without a scheme.");
            if (!scheme.Statuses.Contains(item.Status, StringComparer.OrdinalIgnoreCase))
            {
                throw new ConflictException("WORKFLOW_PUBLISH_EXISTING_SCHEME_INVALID", "Publish would invalidate an existing work item issue scheme.");
            }
        }

        foreach (var board in activeBoards)
        {
            var mappings = board.Columns.SelectMany(column => StatusNames(board, column)
                .Select(status => (Column: column, Status: status))).ToList();
            if (statusNames.Any(status => mappings.Count(x => x.Status.Equals(status, StringComparison.OrdinalIgnoreCase)) != 1))
            {
                throw new ConflictException("WORKFLOW_PUBLISH_BOARD_MAPPING_INVALID", "Every published status must map to exactly one column on each active board.");
            }

            foreach (var item in activeItems.Where(x => x.BoardId == board.Id))
            {
                var column = board.Columns.SingleOrDefault(x => x.Id == item.ColumnId);
                if (column is null || !StatusNames(board, column).Contains(item.Status, StringComparer.OrdinalIgnoreCase))
                {
                    throw new ConflictException("WORKFLOW_PUBLISH_EXISTING_BOARD_INVALID", "Publish would invalidate an existing work item board placement.");
                }
            }
        }
    }

    private static async Task<IReadOnlyCollection<T>> ReadAllAsync<T>(
        IDocumentRepository<T> repository,
        System.Linq.Expressions.Expression<Func<T, bool>> filter,
        CancellationToken ct) where T : class, IDocument
    {
        var result = new List<T>();
        string? cursor = null;
        do
        {
            var page = await repository.ListByCursorAsync(filter, cursor, 200, ct);
            result.AddRange(page.Items);
            cursor = page.NextCursor;
        } while (cursor is not null);
        return result;
    }

    private static IReadOnlyCollection<string> StatusNames(BoardDocument board, BoardColumnDocument column) =>
        board.WorkflowMappingVersion > 0 ? column.StatusNames : [column.Name];
}

public sealed class WorkflowProjectAccessCheckerAdapter(
    IProjectResourcePolicy resourcePolicy) : IWorkflowProjectAccessChecker
{
    public Task EnsureCanViewAsync(string projectId, CancellationToken ct) =>
        EnsureAsync(projectId, manage: false, ct);

    public Task EnsureCanManageAsync(string projectId, CancellationToken ct) =>
        EnsureAsync(projectId, manage: true, ct);

    private async Task EnsureAsync(string projectId, bool manage, CancellationToken ct)
    {
        var permission = manage ? PermissionCatalog.WorkflowManage : PermissionCatalog.WorkflowView;
        _ = await resourcePolicy.AuthorizeAsync(projectId, permission, ct);
    }
}
