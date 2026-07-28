using Zumbo.BuildingBlocks.Application.Events;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed record ProjectMemberRole : ValueObject
{
    private ProjectMemberRole(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public bool IsProjectAdmin => Value == "ProjectAdmin";

    public static ProjectMemberRole Create(string? role)
    {
        if (string.Equals(role, "ProjectAdmin", StringComparison.OrdinalIgnoreCase))
        {
            return new ProjectMemberRole("ProjectAdmin");
        }

        if (string.IsNullOrWhiteSpace(role)
            || string.Equals(role, "Developer", StringComparison.OrdinalIgnoreCase))
        {
            return new ProjectMemberRole("Developer");
        }

        if (string.Equals(role, "Viewer", StringComparison.OrdinalIgnoreCase))
        {
            return new ProjectMemberRole("Viewer");
        }

        throw new ValidationException("Project member role must be ProjectAdmin, Developer or Viewer.");
    }

    public override string ToString() => Value;
}

public sealed record ProjectMemberAddedDomainEvent(
    string ProjectId,
    string OrganizationId,
    string UserId,
    string Role,
    DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record ProjectMemberRoleChangedDomainEvent(
    string ProjectId,
    string UserId,
    string PreviousRole,
    string Role,
    DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record ProjectMemberRemovedDomainEvent(
    string ProjectId,
    string UserId,
    string Role,
    DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record ProjectMemberAddedIntegrationEvent(
    string EventId,
    string AggregateId,
    string OrganizationId,
    string UserId,
    string Role,
    DateTimeOffset OccurredAt) : IIntegrationEvent
{
    public string EventName => "project.member-added.v1";
}

public sealed record ProjectMemberRoleChangedIntegrationEvent(
    string EventId,
    string AggregateId,
    string UserId,
    string PreviousRole,
    string Role,
    DateTimeOffset OccurredAt) : IIntegrationEvent
{
    public string EventName => "project.member-role-changed.v1";
}

public sealed record ProjectMemberRemovedIntegrationEvent(
    string EventId,
    string AggregateId,
    string UserId,
    string Role,
    DateTimeOffset OccurredAt) : IIntegrationEvent
{
    public string EventName => "project.member-removed.v1";
}

public sealed class ProjectMembershipDomainEventMapper :
    IIntegrationEventMapper<ProjectMemberAddedDomainEvent, ProjectMemberAddedIntegrationEvent>,
    IIntegrationEventMapper<ProjectMemberRoleChangedDomainEvent, ProjectMemberRoleChangedIntegrationEvent>,
    IIntegrationEventMapper<ProjectMemberRemovedDomainEvent, ProjectMemberRemovedIntegrationEvent>
{
    public ProjectMemberAddedIntegrationEvent Map(ProjectMemberAddedDomainEvent domainEvent) =>
        new(
            Guid.NewGuid().ToString("N"),
            domainEvent.ProjectId,
            domainEvent.OrganizationId,
            domainEvent.UserId,
            domainEvent.Role,
            domainEvent.OccurredAt);

    public ProjectMemberRoleChangedIntegrationEvent Map(ProjectMemberRoleChangedDomainEvent domainEvent) =>
        new(
            Guid.NewGuid().ToString("N"),
            domainEvent.ProjectId,
            domainEvent.UserId,
            domainEvent.PreviousRole,
            domainEvent.Role,
            domainEvent.OccurredAt);

    public ProjectMemberRemovedIntegrationEvent Map(ProjectMemberRemovedDomainEvent domainEvent) =>
        new(
            Guid.NewGuid().ToString("N"),
            domainEvent.ProjectId,
            domainEvent.UserId,
            domainEvent.Role,
            domainEvent.OccurredAt);
}

public sealed class ProjectMembershipAggregate : AggregateRoot
{
    private readonly ProjectDocument _state;

    private ProjectMembershipAggregate(ProjectDocument state)
    {
        _state = state;
        Id = state.Id;
    }

    public IReadOnlyCollection<ProjectMemberDocument> Members => _state.Members.AsReadOnly();

    public static ProjectMembershipAggregate Rehydrate(ProjectDocument state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new ProjectMembershipAggregate(state);
    }

    public string EnsureCanAddMember(string? userId)
    {
        var normalizedUserId = NormalizeUserId(userId);
        if (_state.Members.Any(member => member.UserId == normalizedUserId))
        {
            throw new ConflictException("PROJECT_MEMBER_EXISTS", "Project member already exists.");
        }

        ProjectCardinalityLimits.EnsureCanGrow(
            _state.Members.Count,
            ProjectCardinalityLimits.MaximumMembers,
            "PROJECT_MEMBER_LIMIT_REACHED",
            "members");
        return normalizedUserId;
    }

    public ProjectMemberAddedDomainEvent AddMember(
        string? userId,
        string? role,
        DateTimeOffset occurredAt) =>
        AddMember(EnsureCanAddMember(userId), ProjectMemberRole.Create(role), occurredAt);

    public ProjectMemberAddedDomainEvent AddMember(
        string userId,
        ProjectMemberRole role,
        DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(role);
        var normalizedUserId = EnsureCanAddMember(userId);
        _state.Members.Add(new ProjectMemberDocument
        {
            UserId = normalizedUserId,
            Role = role.Value
        });
        _state.UpdatedAt = occurredAt;

        var domainEvent = new ProjectMemberAddedDomainEvent(
            _state.Id,
            _state.OrganizationId,
            normalizedUserId,
            role.Value,
            occurredAt);
        Raise(domainEvent);
        return domainEvent;
    }

    public ProjectMemberRoleChangedDomainEvent ChangeMemberRole(
        string? userId,
        string? role,
        DateTimeOffset occurredAt)
    {
        var member = FindMember(userId);
        if (member.Role.Equals("ProjectOwner", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException(
                "PROJECT_OWNER_ROLE_LOCKED",
                "Project owner role cannot be changed through member role update.");
        }

        var previousRole = member.Role;
        member.Role = ProjectMemberRole.Create(role).Value;
        _state.UpdatedAt = occurredAt;

        var domainEvent = new ProjectMemberRoleChangedDomainEvent(
            _state.Id,
            member.UserId,
            previousRole,
            member.Role,
            occurredAt);
        Raise(domainEvent);
        return domainEvent;
    }

    public ProjectMemberRemovedDomainEvent RemoveMember(
        string? userId,
        string actorUserId,
        DateTimeOffset occurredAt)
    {
        var actor = _state.Members.SingleOrDefault(member => member.UserId == actorUserId);
        var member = FindMember(userId);
        if (member.Role.Equals("ProjectOwner", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException("PROJECT_OWNER_CANNOT_BE_REMOVED", "Project owner cannot be removed.");
        }

        if (actor?.Role.Equals("ProjectAdmin", StringComparison.OrdinalIgnoreCase) == true
            && member.Role.Equals("ProjectAdmin", StringComparison.OrdinalIgnoreCase)
            && member.UserId != actor.UserId)
        {
            throw new ForbiddenException("Project admins cannot remove another project admin.");
        }

        _state.Members.Remove(member);
        _state.UpdatedAt = occurredAt;

        var domainEvent = new ProjectMemberRemovedDomainEvent(
            _state.Id,
            member.UserId,
            member.Role,
            occurredAt);
        Raise(domainEvent);
        return domainEvent;
    }

    private ProjectMemberDocument FindMember(string? userId)
    {
        var normalizedUserId = userId?.Trim() ?? string.Empty;
        return _state.Members.SingleOrDefault(member => member.UserId == normalizedUserId)
            ?? throw new NotFoundException("PROJECT_MEMBER_NOT_FOUND", "Project member was not found.");
    }

    private static string NormalizeUserId(string? userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ValidationException("Project member user id is required.");
        }

        return userId.Trim();
    }
}
