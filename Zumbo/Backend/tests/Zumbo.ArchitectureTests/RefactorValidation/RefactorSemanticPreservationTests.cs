namespace Zumbo.ArchitectureTests.RefactorValidation;

public sealed class RefactorSemanticPreservationTests
{
    private static readonly IReadOnlyDictionary<string, string> AcceptedBodyDifferences =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Zumbo.Api|ApiHostRegistration|method:AddZumboHost:(thisWebApplicationBuilderbuilder):WebApplicationBuilder"] =
                "The composition root now delegates, in original order, to responsibility-specific Configure* partial methods; the local dependency-health timeout hardening is classified separately in the runtime contract audit.",
            ["Zumbo.Api|WorkItemEndpoints|method:MapWorkItemEndpoints:(thisRouteGroupBuilderapi):void"] =
                "The route host now delegates each original mapping statement to a route-specific Map* partial method; HTTP route, metadata, authorization, request, response, and handler equivalence is verified by the runtime contract audit.",
            ["Zumbo.Api|IdentityEndpoints|method:AddIdentityModule:(thisIServiceCollectionservices):IServiceCollection"] =
                "Identity handler registrations remain scoped but now use explicit factories so the composition root selects port-focused vertical-slice constructors while legacy constructors remain available for source compatibility.",
            ["Zumbo.Persistence.PostgreSql|Zumbo.Persistence.PostgreSql.PostgreSqlMigrationRunner|method:BuildMigrations:():IReadOnlyList<Migration>"] =
                "Migration SQL constants moved from one method body to private fields while the ordered 37 Migration.Create calls remain in BuildMigrations; IDs, names, SQL, and checksums are verified by the runtime contract audit.",
            ["Zumbo.BuildingBlocks.Infrastructure|Zumbo.BuildingBlocks.Infrastructure.Search.OpenSearchWorkItemSearchIndex|method:InitializeAsync:(CancellationTokencancellationToken=default):Task"] =
                "Initialization now detects the pre-alias concrete index layout and delegates to a lossless, count-verified reindex migration before atomically installing the write alias; unit and live runtime checks cover both success and fail-closed behavior.",
            ["Zumbo.Api|MongoMigrationRunner|method:ApplyIndexesAsync:(stringmigrationId,IReadOnlyList<MongoIndexSpecification>indexes,CancellationTokencancellationToken):Task<MongoMigrationOutcome>"] =
                "Index application preserves catalog checksums while accepting semantically equivalent legacy names and skipping superseded identity and notification definitions whose dedicated later migrations own the valid replacements.",
            ["Zumbo.Api|MongoMigrationRunner|method:RunAsync:(CancellationTokencancellationToken=default):Task<MongoMigrationRunReport>"] =
                "Startup now always runs idempotent compatibility migrations that normalize legacy user document versions and remove infrastructure-only migration markers; optional high-volume business backfills remain explicitly gated.",
            ["Zumbo.Modules.Identity|Zumbo.Modules.Identity.IdentityService|method:RegisterAsync:(RegisterUserRequestrequest,CancellationTokenct):Task<AuthResponse>"] =
                "The compatibility facade delegates registration to the port-focused RegisterUser slice; the original public signature remains available and registration behavior is covered by focused unit and API tests.",
            ["Zumbo.Modules.Identity|Zumbo.Modules.Identity.IdentityService|method:SearchUsersAsync:(string?search,CancellationTokenct):Task<IReadOnlyList<UserProfileResponse>>"] =
                "The compatibility facade delegates user search to its port-focused query slice while preserving the original public signature and tenant authorization behavior.",
            ["Zumbo.Modules.Identity|Zumbo.Modules.Identity.RegisterUserHandler|method:HandleAsync:(RegisterUserRequestrequest,CancellationTokenct):Task<AuthResponse>"] =
                "The endpoint handler now selects the independent registration slice when composed from ports while retaining the original IdentityService constructor as a compatibility path.",
            ["Zumbo.Modules.Identity|Zumbo.Modules.Identity.SearchUsersHandler|method:HandleAsync:(SearchUsersQueryquery,CancellationTokenct):Task<IReadOnlyList<UserProfileResponse>>"] =
                "The endpoint handler now selects the independent user-search slice when composed from ports while retaining the original IdentityService constructor as a compatibility path.",
            ["Zumbo.Api|OrganizationsEndpoints|method:AddOrganizationsModule:(thisIServiceCollectionservices):IServiceCollection"] =
                "Organization create and list handlers remain scoped while explicit factories select their port-focused constructors and preserve compatibility constructors.",
            ["Zumbo.Modules.Organizations|Zumbo.Modules.Organizations.OrganizationService|method:CreateAsync:(CreateOrganizationRequestrequest,stringcorrelationId,CancellationTokenct):Task<OrganizationResponse>"] =
                "The compatibility facade delegates organization creation to the port-focused CreateOrganization slice while preserving its public signature and behavior.",
            ["Zumbo.Modules.Organizations|Zumbo.Modules.Organizations.OrganizationService|method:ListAsync:(CancellationTokenct):Task<IReadOnlyList<OrganizationResponse>>"] =
                "The compatibility facade delegates organization listing to the tenant-scoped ListOrganizations query slice while preserving its public signature.",
            ["Zumbo.Modules.Organizations|Zumbo.Modules.Organizations.CreateOrganizationHandler|method:HandleAsync:(CreateOrganizationRequestrequest,stringcorrelationId,CancellationTokenct):Task<OrganizationResponse>"] =
                "The endpoint handler selects the independent organization-creation slice when composed from ports and retains its original facade constructor.",
            ["Zumbo.Modules.Organizations|Zumbo.Modules.Organizations.ListOrganizationsHandler|method:HandleAsync:(ListOrganizationsQueryquery,CancellationTokenct):Task<IReadOnlyList<OrganizationResponse>>"] =
                "The endpoint handler selects the independent organization-list query slice when composed from ports and retains its original facade constructor.",
            ["Zumbo.Modules.Organizations|Zumbo.Modules.Organizations.OrganizationService|ctor:(IDocumentRepository<OrganizationDocument>organizations,IOrganizationMemberDirectorymemberDirectory,IDistributedLockProviderdistributedLockProvider,IOptions<DistributedLockOptions>distributedLockOptions,IClockclock,ICurrentUsercurrentUser,IOrganizationAuditWriteraudit,IExpectedVersionAccessor?expectedVersions=null,IOptions<OrganizationLifecycleOptions>?lifecycleOptions=null)"] =
                "The unchanged compatibility facade constructor now wires the port-focused create and list handlers from its existing dependencies; its public signature and all previous assignments remain intact.",
            ["Zumbo.Api|TeamsEndpoints|method:AddTeamsModule:(thisIServiceCollectionservices):IServiceCollection"] =
                "Team create and list handlers remain scoped while explicit factories select their port-focused constructors and preserve compatibility constructors.",
            ["Zumbo.Modules.Teams|Zumbo.Modules.Teams.TeamService|method:CreateAsync:(CreateTeamRequestrequest,stringcorrelationId,CancellationTokenct):Task<TeamResponse>"] =
                "The compatibility facade delegates team creation to the port-focused CreateTeam slice while preserving its public signature and behavior.",
            ["Zumbo.Modules.Teams|Zumbo.Modules.Teams.TeamService|method:ListAsync:(stringorganizationId,CancellationTokenct,boolarchived=false):Task<IReadOnlyList<TeamResponse>>"] =
                "The compatibility facade delegates team listing to the tenant-scoped ListTeams query slice while preserving its public signature.",
            ["Zumbo.Modules.Teams|Zumbo.Modules.Teams.CreateTeamHandler|method:HandleAsync:(CreateTeamRequestrequest,stringcorrelationId,CancellationTokenct):Task<TeamResponse>"] =
                "The endpoint handler selects the independent team-creation slice when composed from ports and retains its original facade constructor.",
            ["Zumbo.Modules.Teams|Zumbo.Modules.Teams.ListTeamsHandler|method:HandleAsync:(ListTeamsQueryquery,CancellationTokenct):Task<IReadOnlyList<TeamResponse>>"] =
                "The endpoint handler selects the independent team-list query slice when composed from ports and retains its original facade constructor.",
            ["Zumbo.Modules.Teams|Zumbo.Modules.Teams.TeamService|ctor:(IDocumentRepository<TeamDocument>teams,ITeamUserDirectoryuserDirectory,ITeamAuditWriteraudit,IClockclock,ICurrentUsercurrentUser,IExpectedVersionAccessor?expectedVersions=null,ITeamOrganizationDirectory?organizationDirectory=null,ITeamInvitationNotifier?invitationNotifier=null)"] =
                "The unchanged compatibility facade constructor now wires the port-focused create and list handlers from its existing dependencies; its public signature and all previous assignments remain intact.",
            ["Zumbo.Api|ProjectsEndpoints|method:AddProjectsModule:(thisIServiceCollectionservices):IServiceCollection"] =
                "Project create and list handlers remain scoped while explicit factories select their port-focused constructors and preserve compatibility constructors.",
            ["Zumbo.Modules.Projects|Zumbo.Modules.Projects.ProjectService|method:CreateAsync:(CreateProjectRequestrequest,stringcorrelationId,CancellationTokenct):Task<ProjectResponse>"] =
                "The compatibility facade delegates project creation to the port-focused CreateProject slice while preserving its public signature and behavior.",
            ["Zumbo.Modules.Projects|Zumbo.Modules.Projects.ProjectService|method:ListAsync:(stringorganizationId,CancellationTokenct,boolarchived=false):Task<IReadOnlyList<ProjectResponse>>"] =
                "The compatibility facade delegates project listing to the tenant-scoped ListProjects query slice while preserving its public signature.",
            ["Zumbo.Modules.Projects|Zumbo.Modules.Projects.CreateProjectHandler|method:HandleAsync:(CreateProjectRequestrequest,stringcorrelationId,CancellationTokenct):Task<ProjectResponse>"] =
                "The endpoint handler selects the independent project-creation slice when composed from ports and retains its original facade constructor.",
            ["Zumbo.Modules.Projects|Zumbo.Modules.Projects.ListProjectsHandler|method:HandleAsync:(ListProjectsQueryquery,CancellationTokenct):Task<IReadOnlyList<ProjectResponse>>"] =
                "The endpoint handler selects the independent project-list query slice when composed from ports and retains its original facade constructor.",
            ["Zumbo.Modules.Projects|Zumbo.Modules.Projects.ProjectService|ctor:(IDocumentRepository<ProjectDocument>projects,IProjectMemberDirectorymemberDirectory,IProjectTeamDirectoryteamDirectory,IProjectTeamUsageCheckerteamUsageChecker,IProjectAuditWriteraudit,IClockclock,ICurrentUsercurrentUser,IExpectedVersionAccessor?expectedVersions=null,IProjectOrganizationDirectory?organizationDirectory=null,IOptions<ProjectLifecycleOptions>?lifecycleOptions=null)"] =
                "The unchanged compatibility facade constructor now wires the port-focused create and list handlers from its existing dependencies; its public signature and all previous assignments remain intact.",
            ["Zumbo.Api|BoardsEndpoints|method:AddBoardsModule:(thisIServiceCollectionservices):IServiceCollection"] =
                "Board create and project-list handlers remain scoped while explicit factories select their port-focused constructors and preserve compatibility constructors.",
            ["Zumbo.Modules.Boards|Zumbo.Modules.Boards.BoardService|method:CreateAsync:(CreateBoardRequestrequest,stringcorrelationId,CancellationTokenct):Task<BoardResponse>"] =
                "The compatibility facade delegates board creation to the port-focused CreateBoard slice while preserving its public signature and behavior.",
            ["Zumbo.Modules.Boards|Zumbo.Modules.Boards.BoardService|method:ListByProjectAsync:(stringprojectId,CancellationTokenct,boolarchived=false):Task<IReadOnlyList<BoardResponse>>"] =
                "The compatibility facade delegates board listing to the project-scoped ListBoardsByProject query slice while preserving its public signature.",
            ["Zumbo.Modules.Boards|Zumbo.Modules.Boards.CreateBoardHandler|method:HandleAsync:(CreateBoardRequestrequest,stringcorrelationId,CancellationTokenct):Task<BoardResponse>"] =
                "The endpoint handler selects the independent board-creation slice when composed from ports and retains its original facade constructor.",
            ["Zumbo.Modules.Boards|Zumbo.Modules.Boards.ListBoardsByProjectHandler|method:HandleAsync:(ListBoardsByProjectQueryquery,CancellationTokenct):Task<IReadOnlyList<BoardResponse>>"] =
                "The endpoint handler selects the independent project-board-list slice when composed from ports and retains its original facade constructor.",
            ["Zumbo.Modules.Boards|Zumbo.Modules.Boards.BoardService|ctor:(IDocumentRepository<BoardDocument>boards,IBoardProjectAccessCheckeraccessChecker,IBoardColumnUsageCheckerusageChecker,IDistributedLockProviderdistributedLockProvider,IOptions<DistributedLockOptions>distributedLockOptions,IClockclock,ICurrentUsercurrentUser,IBoardAuditWriteraudit,IExpectedVersionAccessor?expectedVersions=null,IBoardWorkflowCatalog?workflowCatalog=null)"] =
                "The unchanged compatibility facade constructor now wires the port-focused create and list handlers from its existing dependencies; its public signature and previous expected-version assignment remain intact.",
            ["Zumbo.Api|WorkflowEndpoints|method:AddWorkflowsModule:(thisIServiceCollectionservices,IConfiguration?configuration=null):IServiceCollection"] =
                "Workflow upsert and read handlers remain scoped while explicit factories select their port-focused constructors and preserve compatibility constructors.",
            ["Zumbo.Modules.Workflows|Zumbo.Modules.Workflows.WorkflowService|method:UpsertAsync:(CreateWorkflowRequestrequest,stringcorrelationId,CancellationTokenct):Task<WorkflowResponse>"] =
                "The compatibility facade delegates workflow upsert to the port-focused UpsertWorkflow slice while preserving its public signature and behavior.",
            ["Zumbo.Modules.Workflows|Zumbo.Modules.Workflows.WorkflowService|method:GetOrCreateDefaultAsync:(stringprojectId,CancellationTokenct):Task<WorkflowResponse>"] =
                "The compatibility facade delegates workflow retrieval and default creation to the project-scoped GetWorkflow slice while preserving its public signature.",
            ["Zumbo.Modules.Workflows|Zumbo.Modules.Workflows.UpsertWorkflowHandler|method:HandleAsync:(CreateWorkflowRequestrequest,stringcorrelationId,CancellationTokenct):Task<WorkflowResponse>"] =
                "The endpoint handler selects the independent workflow-upsert slice when composed from ports and retains its original facade constructor.",
            ["Zumbo.Modules.Workflows|Zumbo.Modules.Workflows.GetWorkflowHandler|method:HandleAsync:(GetWorkflowQueryquery,CancellationTokenct):Task<WorkflowResponse>"] =
                "The endpoint handler selects the independent workflow-read slice when composed from ports and retains its original facade constructor.",
            ["Zumbo.Modules.Workflows|Zumbo.Modules.Workflows.WorkflowService|ctor:(IDocumentRepository<WorkflowDefinitionDocument>workflows,IWorkflowProjectAccessCheckeraccessChecker,IDistributedLockProviderdistributedLockProvider,IOptions<DistributedLockOptions>distributedLockOptions,IClockclock,IWorkflowAuditWriteraudit,IExpectedVersionAccessor?expectedVersions=null,IWorkflowPublicationGuard?publicationGuard=null)"] =
                "The unchanged compatibility facade constructor now wires port-focused upsert and read handlers from its existing dependencies; its public signature and expected-version state remain intact.",
            ["Zumbo.Api|WorkItemEndpoints|method:AddWorkItemsModule:(thisIServiceCollectionservices,IConfiguration?configuration=null):IServiceCollection"] =
                "Work-item create and search handlers remain scoped while explicit factories select their port-focused constructors and preserve compatibility constructors.",
            ["Zumbo.Modules.WorkItems|Zumbo.Modules.WorkItems.CreateWorkItemHandler|method:HandleAsync:(CreateWorkItemRequestrequest,stringcorrelationId,CancellationTokenct):Task<WorkItemResponse>"] =
                "The endpoint handler selects the independent work-item creation slice when composed from ports and retains its original WorkItemService constructor as a compatibility path.",
            ["Zumbo.Modules.WorkItems|Zumbo.Modules.WorkItems.SearchWorkItemsHandler|method:HandleAsync:(WorkItemSearchRequestrequest,CancellationTokenct):Task<IReadOnlyList<WorkItemResponse>>"] =
                "The endpoint handler selects the independent work-item search slice when composed from ports and retains its original WorkItemService constructor as a compatibility path.",
            ["Zumbo.Modules.WorkItems|Zumbo.Modules.WorkItems.WorkItemService|method:CreateAsync:(CreateWorkItemRequestrequest,stringcorrelationId,CancellationTokenct,string?requestedId=null):Task<WorkItemResponse>"] =
                "The compatibility facade delegates ordinary work-item creation to the port-focused slice while preserving the requested-id path used by legacy and intake-compatible callers.",
            ["Zumbo.Modules.WorkItems|Zumbo.Modules.WorkItems.WorkItemService|method:SearchAsync:(WorkItemSearchRequestrequest,CancellationTokenct):Task<IReadOnlyList<WorkItemResponse>>"] =
                "The compatibility facade delegates work-item search to the port-focused query slice while preserving the original public signature and tenant-scoped behavior."
        };

    private static string ProjectDirectory => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

    private static string RepositoryDirectory => Directory.GetParent(ProjectDirectory)!.FullName;

    private static string ReportDirectory => Path.Combine(ProjectDirectory, "docs", "architecture");

    [Fact]
    public void RefactorSnapshot_PreservesBaselineSemanticInventoryAndNormalizedBodies()
    {
        var baseline = RefactorSemanticInventory.ReadGitSnapshot(
            RepositoryDirectory,
            RefactorSemanticInventory.BaselineCommit);
        var target = RefactorSemanticInventory.ReadWorkingTree(ProjectDirectory);
        var comparison = RefactorSemanticInventory.Compare(baseline, target);
        var reports = RefactorValidationReportBuilder.Build(comparison, AcceptedBodyDifferences);

        if (Environment.GetEnvironmentVariable("ZUMBO_UPDATE_REFACTOR_REPORTS") == "1")
        {
            Directory.CreateDirectory(ReportDirectory);
            foreach (var (fileName, content) in reports)
            {
                File.WriteAllText(Path.Combine(ReportDirectory, fileName), content);
            }
        }

        foreach (var (fileName, expected) in reports)
        {
            var path = Path.Combine(ReportDirectory, fileName);
            Assert.True(File.Exists(path), $"Missing generated refactor validation report: {fileName}");
            Assert.Equal(expected, File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal));
        }

        Assert.Empty(comparison.MissingTypes);
        Assert.Empty(comparison.TypeSignatureDifferences);
        Assert.Empty(comparison.MissingMembers);
        Assert.Empty(comparison.MemberSignatureDifferences);

        var unexplainedBodyDifferences = comparison.BodyDifferences
            .Where(item => !AcceptedBodyDifferences.ContainsKey(item.Id))
            .Select(item => item.Id)
            .ToArray();
        Assert.Empty(unexplainedBodyDifferences);
    }
}
