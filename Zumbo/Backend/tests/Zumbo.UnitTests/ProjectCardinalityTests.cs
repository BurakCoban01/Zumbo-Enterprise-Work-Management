using System.Text.Json;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.Projects;
using Zumbo.SharedKernel;

namespace Zumbo.UnitTests;

public sealed class ProjectCardinalityTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 29, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("member", ProjectCardinalityLimits.MaximumMembers, "PROJECT_MEMBER_LIMIT_REACHED")]
    [InlineData("team", ProjectCardinalityLimits.MaximumTeams, "PROJECT_TEAM_LIMIT_REACHED")]
    [InlineData("template", ProjectCardinalityLimits.MaximumTemplates, "PROJECT_TEMPLATE_LIMIT_REACHED")]
    [InlineData("component", ProjectCardinalityLimits.MaximumComponents, "PROJECT_COMPONENT_LIMIT_REACHED")]
    [InlineData("version", ProjectCardinalityLimits.MaximumVersions, "PROJECT_VERSION_LIMIT_REACHED")]
    [InlineData("release", ProjectCardinalityLimits.MaximumReleases, "PROJECT_RELEASE_LIMIT_REACHED")]
    [InlineData("milestone", ProjectCardinalityLimits.MaximumMilestones, "PROJECT_MILESTONE_LIMIT_REACHED")]
    public async Task EmbeddedCollectionGrowth_RejectsAtLimitWithoutChangingStoredProject(
        string collection,
        int maximum,
        string expectedCode)
    {
        var repository = new InMemoryDocumentRepository<ProjectDocument>();
        var project = ProjectAtLimit(collection, maximum);
        await repository.CreateAsync(project);
        var service = CreateService(repository);

        var exception = await Assert.ThrowsAsync<ConflictException>(() =>
            GrowAsync(service, project.Id, collection));

        Assert.Equal(expectedCode, exception.Code);
        var persisted = await repository.SelectAsync(candidate => candidate.Id == project.Id);
        Assert.NotNull(persisted);
        Assert.Equal(1, persisted!.Version);
        Assert.Equal(maximum, CollectionCount(persisted, collection));
    }

    [Fact]
    public void MaximumEmbeddedCardinality_FitsTwoMebibyteSerializedDocumentBudget()
    {
        var project = MaximumSizedProject();

        var serializedBytes = JsonSerializer.SerializeToUtf8Bytes(project).Length;

        Assert.InRange(serializedBytes, 1, ProjectCardinalityLimits.MaximumSerializedBytes);
    }

    private static ProjectService CreateService(
        InMemoryDocumentRepository<ProjectDocument> repository) =>
        new(
            repository,
            new AllowMembers(),
            new ActiveTeams(),
            new NoTeamUsage(),
            new NoAudit(),
            new FixedClock(Now),
            new CurrentUserStub
            {
                UserId = "owner-1",
                OrganizationId = "org-1"
            });

    private static async Task GrowAsync(
        ProjectService service,
        string projectId,
        string collection)
    {
        switch (collection)
        {
            case "member":
                await service.AddMemberAsync(
                    projectId,
                    new AddProjectMemberRequest("new-member", ProjectRoles.Viewer),
                    CancellationToken.None);
                break;
            case "team":
                await service.AddTeamAsync(
                    projectId,
                    new AddProjectTeamRequest("new-team"),
                    CancellationToken.None);
                break;
            case "template":
                await service.UpsertTemplateAsync(
                    projectId,
                    null,
                    new UpsertProjectTemplateRequest("New template", false),
                    "test",
                    CancellationToken.None);
                break;
            case "component":
                await service.CreateComponentAsync(
                    projectId,
                    new CreateProjectComponentRequest("New component"),
                    "test",
                    CancellationToken.None);
                break;
            case "version":
                await service.CreateVersionAsync(
                    projectId,
                    new CreateProjectVersionRequest("New version"),
                    "test",
                    CancellationToken.None);
                break;
            case "release":
                await service.CreateReleaseAsync(
                    projectId,
                    new CreateProjectReleaseRequest("new-version", "New release"),
                    "test",
                    CancellationToken.None);
                break;
            case "milestone":
                await service.CreateMilestoneAsync(
                    projectId,
                    new CreateProjectMilestoneRequest("New milestone", Now.AddDays(30)),
                    "test",
                    CancellationToken.None);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(collection));
        }
    }

    private static ProjectDocument ProjectAtLimit(string collection, int maximum)
    {
        var project = BaseProject();
        switch (collection)
        {
            case "member":
                project.Members.AddRange(Enumerable.Range(1, maximum - 1)
                    .Select(index => new ProjectMemberDocument
                    {
                        UserId = $"member-{index:D4}",
                        Role = ProjectRoles.Viewer
                    }));
                break;
            case "team":
                project.TeamIds.AddRange(Enumerable.Range(1, maximum)
                    .Select(index => $"team-{index:D4}"));
                break;
            case "template":
                project.Templates.AddRange(Enumerable.Range(1, maximum)
                    .Select(index => new ProjectTemplateDocument
                    {
                        Name = $"Template {index:D4}",
                        IsDefault = index == 1
                    }));
                break;
            case "component":
                project.Components.AddRange(Enumerable.Range(1, maximum)
                    .Select(index => new ProjectComponentDocument
                    {
                        Name = $"Component {index:D4}"
                    }));
                break;
            case "version":
                project.Versions.AddRange(Enumerable.Range(1, maximum)
                    .Select(index => new ProjectVersionDocument
                    {
                        Id = $"version-{index:D4}",
                        Name = $"Version {index:D4}"
                    }));
                break;
            case "release":
                project.Versions.AddRange(Enumerable.Range(1, maximum + 1)
                    .Select(index => new ProjectVersionDocument
                    {
                        Id = index == maximum + 1 ? "new-version" : $"version-{index:D4}",
                        Name = $"Version {index:D4}"
                    }));
                project.Releases.AddRange(Enumerable.Range(1, maximum)
                    .Select(index => new ProjectReleaseDocument
                    {
                        VersionId = $"version-{index:D4}",
                        Name = $"Release {index:D4}"
                    }));
                break;
            case "milestone":
                project.Milestones.AddRange(Enumerable.Range(1, maximum)
                    .Select(index => new ProjectMilestoneDocument
                    {
                        Name = $"Milestone {index:D4}",
                        DueAt = Now.AddDays(index)
                    }));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(collection));
        }

        return project;
    }

    private static ProjectDocument MaximumSizedProject()
    {
        var project = BaseProject();
        var maximumName = new string('N', 120);
        var maximumDescription = new string('D', 500);
        project.Members.AddRange(Enumerable.Range(1, ProjectCardinalityLimits.MaximumMembers - 1)
            .Select(index => new ProjectMemberDocument
            {
                UserId = $"{new string('u', 80)}-{index:D4}",
                Role = ProjectRoles.Admin
            }));
        project.TeamIds.AddRange(Enumerable.Range(1, ProjectCardinalityLimits.MaximumTeams)
            .Select(index => $"{new string('t', 80)}-{index:D4}"));
        project.Templates.AddRange(Enumerable.Range(1, ProjectCardinalityLimits.MaximumTemplates)
            .Select(index => new ProjectTemplateDocument
            {
                Name = maximumName,
                IsDefault = index == 1,
                DefaultComponentNames = Enumerable.Range(1, ProjectCatalogLimits.MaximumDefaultComponentNames)
                    .Select(component => $"{new string('c', 76)}-{component:D2}")
                    .ToList()
            }));
        project.Components.AddRange(Enumerable.Range(1, ProjectCardinalityLimits.MaximumComponents)
            .Select(_ => new ProjectComponentDocument
            {
                Name = new string('C', 80),
                Description = maximumDescription
            }));
        project.Versions.AddRange(Enumerable.Range(1, ProjectCardinalityLimits.MaximumVersions)
            .Select(index => new ProjectVersionDocument
            {
                Id = $"version-{index:D4}",
                Name = new string('V', 80)
            }));
        project.Releases.AddRange(Enumerable.Range(1, ProjectCardinalityLimits.MaximumReleases)
            .Select(index => new ProjectReleaseDocument
            {
                VersionId = $"version-{index:D4}",
                Name = new string('R', 100)
            }));
        project.Milestones.AddRange(Enumerable.Range(1, ProjectCardinalityLimits.MaximumMilestones)
            .Select(_ => new ProjectMilestoneDocument
            {
                Name = maximumName,
                DueAt = Now.AddYears(1)
            }));
        return project;
    }

    private static ProjectDocument BaseProject() => new()
    {
        Id = "project-cardinality",
        OrganizationId = "org-1",
        Key = "CARD",
        Name = "Cardinality project",
        Visibility = ProjectVisibilities.Private,
        Members =
        [
            new ProjectMemberDocument
            {
                UserId = "owner-1",
                Role = ProjectRoles.Owner
            }
        ],
        CreatedAt = Now,
        UpdatedAt = Now
    };

    private static int CollectionCount(ProjectDocument project, string collection) =>
        collection switch
        {
            "member" => project.Members.Count,
            "team" => project.TeamIds.Count,
            "template" => project.Templates.Count,
            "component" => project.Components.Count,
            "version" => project.Versions.Count,
            "release" => project.Releases.Count,
            "milestone" => project.Milestones.Count,
            _ => throw new ArgumentOutOfRangeException(nameof(collection))
        };

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class CurrentUserStub : ICurrentUser
    {
        public string UserId { get; set; } = string.Empty;
        public string OrganizationId { get; set; } = string.Empty;
        public IReadOnlyCollection<string> Roles { get; set; } = ["User"];
    }

    private sealed class AllowMembers : IProjectMemberDirectory
    {
        public Task EnsureEligibleAsync(string userId, string organizationId, CancellationToken ct) =>
            Task.CompletedTask;
    }

    private sealed class ActiveTeams : IProjectTeamDirectory
    {
        public Task<ProjectTeamDirectoryEntry?> FindAsync(string teamId, CancellationToken ct) =>
            Task.FromResult<ProjectTeamDirectoryEntry?>(new(teamId, "org-1", true));
    }

    private sealed class NoTeamUsage : IProjectTeamUsageChecker
    {
        public Task<bool> HasWorkItemsAsync(string projectId, string teamId, CancellationToken ct) =>
            Task.FromResult(false);
    }

    private sealed class NoAudit : IProjectAuditWriter
    {
        public Task WriteAsync(
            string action,
            string entityId,
            string? oldValue,
            string? newValue,
            string correlationId,
            CancellationToken ct) => Task.CompletedTask;
    }
}
