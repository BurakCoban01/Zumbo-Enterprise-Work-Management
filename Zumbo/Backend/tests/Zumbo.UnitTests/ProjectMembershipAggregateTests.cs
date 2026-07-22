using Zumbo.Modules.Projects;
using Zumbo.SharedKernel;

namespace Zumbo.UnitTests;

public sealed class ProjectMembershipAggregateTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(null, "Developer")]
    [InlineData("", "Developer")]
    [InlineData("developer", "Developer")]
    [InlineData("PROJECTADMIN", "ProjectAdmin")]
    [InlineData("viewer", "Viewer")]
    public void ProjectMemberRole_NormalizesAssignableRoles(string? input, string expected)
    {
        var role = ProjectMemberRole.Create(input);

        Assert.Equal(expected, role.Value);
        Assert.Equal(expected, role.ToString());
        Assert.Equal(expected == "ProjectAdmin", role.IsProjectAdmin);
    }

    [Fact]
    public void ProjectMemberRole_RejectsUnsupportedRoleWithExistingValidationContract()
    {
        var exception = Assert.Throws<ValidationException>(() => ProjectMemberRole.Create("ProjectOwner"));

        Assert.Equal("VALIDATION_ERROR", exception.Code);
        Assert.Equal("Project member role must be ProjectAdmin, Developer or Viewer.", exception.Message);
    }

    [Fact]
    public void AddMember_MutatesPersistenceStateAndRaisesDomainEvent()
    {
        var state = NewProject();
        var aggregate = ProjectMembershipAggregate.Rehydrate(state);

        var domainEvent = aggregate.AddMember("  user-2  ", "viewer", Now);

        var member = Assert.Single(state.Members, item => item.UserId == "user-2");
        Assert.Equal("Viewer", member.Role);
        Assert.Equal(Now, state.UpdatedAt);
        Assert.Same(domainEvent, Assert.Single(aggregate.DomainEvents));
        Assert.Equal(state.Id, domainEvent.ProjectId);
        Assert.Equal(state.OrganizationId, domainEvent.OrganizationId);
        Assert.Equal("user-2", domainEvent.UserId);
        Assert.Equal("Viewer", domainEvent.Role);
        Assert.Equal(Now, domainEvent.OccurredAt);
    }

    [Fact]
    public void AddMember_RejectsDuplicateBeforeChangingStateOrRaisingEvent()
    {
        var state = NewProject(new ProjectMemberDocument { UserId = "user-2", Role = "Developer" });
        var aggregate = ProjectMembershipAggregate.Rehydrate(state);

        var exception = Assert.Throws<ConflictException>(() =>
            aggregate.AddMember(" user-2 ", "Viewer", Now));

        Assert.Equal("PROJECT_MEMBER_EXISTS", exception.Code);
        Assert.Equal("Project member already exists.", exception.Message);
        Assert.Equal(2, state.Members.Count);
        Assert.Empty(aggregate.DomainEvents);
    }

    [Fact]
    public void ChangeMemberRole_NormalizesRoleAndRaisesDomainEvent()
    {
        var state = NewProject(new ProjectMemberDocument { UserId = "user-2", Role = "Developer" });
        var aggregate = ProjectMembershipAggregate.Rehydrate(state);

        var domainEvent = aggregate.ChangeMemberRole(" user-2 ", "PROJECTADMIN", Now);

        Assert.Equal("ProjectAdmin", state.Members.Single(item => item.UserId == "user-2").Role);
        Assert.Equal(Now, state.UpdatedAt);
        Assert.Same(domainEvent, Assert.Single(aggregate.DomainEvents));
        Assert.Equal("Developer", domainEvent.PreviousRole);
        Assert.Equal("ProjectAdmin", domainEvent.Role);
    }

    [Fact]
    public void ChangeMemberRole_RejectsOwnerWithExistingConflictContract()
    {
        var state = NewProject();
        var aggregate = ProjectMembershipAggregate.Rehydrate(state);

        var exception = Assert.Throws<ConflictException>(() =>
            aggregate.ChangeMemberRole("owner-1", "Viewer", Now));

        Assert.Equal("PROJECT_OWNER_ROLE_LOCKED", exception.Code);
        Assert.Equal("Project owner role cannot be changed through member role update.", exception.Message);
        Assert.Equal("ProjectOwner", state.Members[0].Role);
        Assert.Empty(aggregate.DomainEvents);
    }

    [Fact]
    public void RemoveMember_RejectsOwnerAndAdminRemovingAnotherAdmin()
    {
        var state = NewProject(
            new ProjectMemberDocument { UserId = "admin-1", Role = "ProjectAdmin" },
            new ProjectMemberDocument { UserId = "admin-2", Role = "ProjectAdmin" });
        var aggregate = ProjectMembershipAggregate.Rehydrate(state);

        var ownerException = Assert.Throws<ConflictException>(() =>
            aggregate.RemoveMember("owner-1", "admin-1", Now));
        var adminException = Assert.Throws<ForbiddenException>(() =>
            aggregate.RemoveMember("admin-2", "admin-1", Now));

        Assert.Equal("PROJECT_OWNER_CANNOT_BE_REMOVED", ownerException.Code);
        Assert.Equal("Project owner cannot be removed.", ownerException.Message);
        Assert.Equal("FORBIDDEN", adminException.Code);
        Assert.Equal("Project admins cannot remove another project admin.", adminException.Message);
        Assert.Equal(3, state.Members.Count);
        Assert.Empty(aggregate.DomainEvents);
    }

    [Fact]
    public void RemoveMember_AllowsAdminToRemoveSelfAndRaisesDomainEvent()
    {
        var state = NewProject(new ProjectMemberDocument { UserId = "admin-1", Role = "ProjectAdmin" });
        var aggregate = ProjectMembershipAggregate.Rehydrate(state);

        var domainEvent = aggregate.RemoveMember(" admin-1 ", "admin-1", Now);

        Assert.DoesNotContain(state.Members, member => member.UserId == "admin-1");
        Assert.Equal(Now, state.UpdatedAt);
        Assert.Same(domainEvent, Assert.Single(aggregate.DomainEvents));
        Assert.Equal("admin-1", domainEvent.UserId);
        Assert.Equal("ProjectAdmin", domainEvent.Role);
    }

    [Fact]
    public void DomainEventMapperCreatesVersionedProviderNeutralContracts()
    {
        var mapper = new ProjectMembershipDomainEventMapper();

        var added = mapper.Map(new ProjectMemberAddedDomainEvent(
            "project-1", "org-1", "user-2", "Developer", Now));
        var changed = mapper.Map(new ProjectMemberRoleChangedDomainEvent(
            "project-1", "user-2", "Developer", "Viewer", Now));
        var removed = mapper.Map(new ProjectMemberRemovedDomainEvent(
            "project-1", "user-2", "Viewer", Now));

        Assert.Equal("project.member-added.v1", added.EventName);
        Assert.Equal("project.member-role-changed.v1", changed.EventName);
        Assert.Equal("project.member-removed.v1", removed.EventName);
        Assert.All(new[] { added.EventId, changed.EventId, removed.EventId }, eventId =>
            Assert.Equal(32, eventId.Length));
        Assert.All(new[] { added.AggregateId, changed.AggregateId, removed.AggregateId }, aggregateId =>
            Assert.Equal("project-1", aggregateId));
        Assert.Equal("org-1", added.OrganizationId);
        Assert.Equal("Developer", changed.PreviousRole);
        Assert.Equal("Viewer", changed.Role);
        Assert.Equal("Viewer", removed.Role);
        Assert.Equal(Now, added.OccurredAt);
        Assert.Equal(Now, changed.OccurredAt);
        Assert.Equal(Now, removed.OccurredAt);
    }

    private static ProjectDocument NewProject(params ProjectMemberDocument[] members) => new()
    {
        Id = "project-1",
        OrganizationId = "org-1",
        Key = "PRJ",
        Name = "Project",
        Members =
        [
            new ProjectMemberDocument { UserId = "owner-1", Role = "ProjectOwner" },
            .. members
        ]
    };
}
