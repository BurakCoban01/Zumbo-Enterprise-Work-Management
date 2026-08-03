using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Zumbo.BuildingBlocks.Application.Events;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Identity;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.ArchitectureTests;

public sealed class ArchitectureBoundaryTests
{
    private static readonly string[] ExpectedModuleInfrastructureViolations = [];

    private static readonly string[] ExpectedCrossModuleViolations = [];

    private static string BackendDirectory => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static string SourceDirectory => Path.Combine(BackendDirectory, "src");

    [Fact]
    public void DomainAndApplicationTypes_DoNotUseMongoAttributes()
    {
        var assemblies = new[]
        {
            typeof(UserDocument).Assembly,
            typeof(WorkItemDocument).Assembly,
            typeof(Entity).Assembly
        };

        var offendingTypes = assemblies
            .SelectMany(x => x.GetTypes())
            .Where(x => x.GetCustomAttributesData()
                .Any(attribute => attribute.AttributeType.Namespace?.StartsWith("MongoDB.Bson", StringComparison.Ordinal) == true))
            .Select(x => x.FullName)
            .ToList();

        Assert.Empty(offendingTypes);
    }

    [Fact]
    public void DocumentRepository_ExposesExpressionBasedCrudWithoutMongoDriverTypes()
    {
        var interfaceType = typeof(IDocumentRepository<>);
        var methods = interfaceType.GetMethods().Select(x => x.Name).ToArray();

        Assert.Contains("CreateAsync", methods);
        Assert.Contains("SelectAsync", methods);
        Assert.Contains("ListByFilterAsync", methods);
        Assert.Contains("ListByCursorAsync", methods);
        Assert.Contains("CountByFilterAsync", methods);
        Assert.Contains("ExistsByFilterAsync", methods);
        Assert.Contains("DeleteByFilterAsync", methods);
        Assert.Contains("ReplaceByFilterAsync", methods);
        Assert.Contains("UpdateOneFieldByFilterAsync", methods);

        Assert.Equal(
            typeof(Task<DocumentMutationResult>),
            interfaceType.GetMethod("ReplaceByFilterAsync")!.ReturnType);
        Assert.Equal(
            typeof(Task<DocumentMutationResult>),
            interfaceType.GetMethod("UpdateOneFieldByFilterAsync")!.ReturnType);

        var publicSignatureTypes = interfaceType
            .GetMethods()
            .SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType))
            .ToList();

        Assert.Contains(publicSignatureTypes, type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Expression<>));
        Assert.DoesNotContain(publicSignatureTypes, type => type.Namespace?.StartsWith("MongoDB.Driver", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void InfrastructureRepositoryCompatibilityContract_InheritsApplicationPort()
    {
        var legacyContract = typeof(Zumbo.BuildingBlocks.Infrastructure.Persistence.IDocumentRepository<>);

        Assert.Contains(
            legacyContract.GetInterfaces(),
            contract => contract.IsGenericType
                && contract.GetGenericTypeDefinition() == typeof(IDocumentRepository<>));
    }

    [Fact]
    public void ModuleSpecificAppsettings_FilesExist()
    {
        var apiDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Zumbo.Api"));

        Assert.True(File.Exists(Path.Combine(apiDir, "appsettings.Identity.json")));
        Assert.True(File.Exists(Path.Combine(apiDir, "appsettings.Boards.json")));
        Assert.True(File.Exists(Path.Combine(apiDir, "appsettings.WorkItems.json")));
    }

    [Fact]
    public void DomainAndApplicationProjects_InfrastructureReferencesMatchExactMigrationAllowList()
    {
        var actual = ModuleProjectFiles()
            .SelectMany(project => ProjectReferences(project)
                .Where(reference => reference == "Zumbo.BuildingBlocks.Infrastructure")
                .Select(reference => $"{ProjectName(project)}->{reference}"));

        AssertExactSet(ExpectedModuleInfrastructureViolations, actual);
    }

    [Fact]
    public void ModuleToModuleReferences_MatchExactMigrationAllowList()
    {
        var actual = ModuleProjectFiles()
            .SelectMany(project => ProjectReferences(project)
                .Where(reference => reference.StartsWith("Zumbo.Modules.", StringComparison.Ordinal))
                .Select(reference => $"{ProjectName(project)}->{reference}"));

        AssertExactSet(ExpectedCrossModuleViolations, actual);
    }

    [Fact]
    public void ApiHostBusinessLogic_RemainsInsideEndpointAndPipelineBoundaries()
    {
        var routeMarkers = new[]
        {
            ".MapGet(", ".MapPost(", ".MapPut(", ".MapPatch(", ".MapDelete("
        };

        var apiDirectory = Path.Combine(SourceDirectory, "Zumbo.Api");
        var actual = Directory.GetFiles(apiDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(file =>
            {
                var source = File.ReadAllText(file);
                return routeMarkers.Any(source.Contains)
                    || source.Contains(": AggregateRoot", StringComparison.Ordinal)
                    || source.Contains(": Entity", StringComparison.Ordinal);
            })
            .Select(file => Path.GetRelativePath(apiDirectory, file).Replace('\\', '/'));

        Assert.NotEmpty(actual);
        Assert.All(
            actual,
            relativePath => Assert.True(
                relativePath.StartsWith("Endpoints/", StringComparison.Ordinal)
                    || relativePath == "Hosting/ApiPipeline.cs",
                $"API business logic must remain inside endpoint or pipeline boundaries: {relativePath}"));
    }

    [Fact]
    public void Program_IsAThinCompositionEntryPointWithoutBusinessLogic()
    {
        var program = Path.Combine(SourceDirectory, "Zumbo.Api", "Program.cs");
        var source = File.ReadAllText(program);
        var forbiddenMarkers = new[]
        {
            ".MapGet(", ".MapPost(", ".MapPut(", ".MapPatch(", ".MapDelete(",
            ".AddScoped<", ".AddSingleton<", ".AddHostedService<", "async ", "throw new ", "if ("
        };

        Assert.True(File.ReadLines(program).Count() <= 32, "Program.cs must remain a thin composition entry point.");
        Assert.All(forbiddenMarkers, marker => Assert.DoesNotContain(marker, source, StringComparison.Ordinal));
    }

    [Fact]
    public void Gateway_IsAThinEdgeHostWithoutDomainOrApiReferences()
    {
        var gatewayDirectory = Path.Combine(SourceDirectory, "Zumbo.Gateway");
        var project = Path.Combine(gatewayDirectory, "Zumbo.Gateway.csproj");
        var program = Path.Combine(gatewayDirectory, "Program.cs");
        var source = File.ReadAllText(program);

        Assert.Empty(ProjectReferences(project));
        Assert.True(File.ReadLines(program).Count() <= 12, "Gateway Program.cs must remain a thin composition entry point.");
        Assert.DoesNotContain(".Map", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".AddScoped<", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Zumbo.Modules", File.ReadAllText(project), StringComparison.Ordinal);
        Assert.DoesNotContain("Zumbo.Api", File.ReadAllText(project), StringComparison.Ordinal);
    }

    [Fact]
    public void ApiPipeline_PreservesExactMiddlewareOrder()
    {
        var pipeline = Path.Combine(SourceDirectory, "Zumbo.Api", "Hosting", "ApiPipeline.cs");
        var actual = File.ReadLines(pipeline)
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("app.Use", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(
        [
            "app.UseForwardedHeaders();",
            "app.UseMiddleware<SecurityHeadersMiddleware>();",
            "app.UseMiddleware<AccessTokenRedactionMiddleware>();",
            "app.UseMiddleware<RequestTelemetryMiddleware>();",
            "app.UseMiddleware<ApiExceptionMiddleware>();",
            "app.UseMiddleware<RequestAbuseProtectionMiddleware>();",
            "app.UseSwagger();",
            "app.UseSwaggerUI();",
            "app.UseCors(\"LocalFrontends\");",
            "app.UseMiddleware<BrowserSessionSecurityMiddleware>();",
            "app.UseAuthentication();",
            "app.UseMiddleware<RedisRateLimitingMiddleware>();",
            "app.UseRateLimiter();",
            "app.UseAuthorization();",
            "app.UseMiddleware<EndpointPermissionMiddleware>();"
        ], actual);
    }

    [Fact]
    public void EndpointHostFiles_ReferenceOnlyTheirOwningModule()
    {
        var expected = new Dictionary<string, string?>
        {
            ["AuditEndpoints.cs"] = "Zumbo.Modules.Audit",
            ["AutomationEndpoints.cs"] = "Zumbo.Modules.Workflows",
            ["BoardsEndpoints.cs"] = "Zumbo.Modules.Boards",
            ["CapacityPlanningEndpoints.cs"] = "Zumbo.Modules.WorkItems",
            ["DashboardEndpoints.cs"] = "Zumbo.Modules.WorkItems",
            ["DevelopmentIntegrationEndpoints.cs"] = "Zumbo.Modules.WorkItems",
            ["GoalEndpoints.cs"] = "Zumbo.Modules.Projects",
            ["IdentityEndpoints.cs"] = "Zumbo.Modules.Identity",
            ["IntakeEndpoints.cs"] = "Zumbo.Modules.WorkItems",
            ["KnowledgeEndpoints.cs"] = "Zumbo.Modules.Projects",
            ["NotificationEndpoints.cs"] = "Zumbo.Modules.Notifications",
            ["OperationsEndpoints.cs"] = null,
            ["OrganizationsEndpoints.cs"] = "Zumbo.Modules.Organizations",
            ["PortfolioEndpoints.cs"] = "Zumbo.Modules.Projects",
            ["ProjectsEndpoints.cs"] = "Zumbo.Modules.Projects",
            ["SprintEndpoints.cs"] = "Zumbo.Modules.WorkItems",
            ["TeamsEndpoints.cs"] = "Zumbo.Modules.Teams",
            ["WebhookEndpoints.cs"] = "Zumbo.Modules.WorkItems",
            ["WorkflowEndpoints.cs"] = "Zumbo.Modules.Workflows",
            ["WorkItemEndpoints.cs"] = "Zumbo.Modules.WorkItems",
            ["WorkItemTypeSchemaEndpoints.cs"] = "Zumbo.Modules.WorkItems"
        };
        var endpointDirectory = Path.Combine(SourceDirectory, "Zumbo.Api", "Endpoints");

        AssertExactSet(expected.Keys, Directory.GetFiles(endpointDirectory, "*.cs").Select(Path.GetFileName).OfType<string>());
        foreach (var (fileName, owningModule) in expected)
        {
            var moduleUsings = File.ReadLines(Path.Combine(endpointDirectory, fileName))
                .Select(line => line.Trim())
                .Where(line => line.StartsWith("using Zumbo.Modules.", StringComparison.Ordinal))
                .Select(line => line["using ".Length..^1]);

            AssertExactSet(owningModule is null ? [] : [owningModule], moduleUsings);
        }
    }

    [Fact]
    public void ModuleCoreAssemblies_DoNotReferenceProviderPackages()
    {
        var providerPrefixes = new[]
        {
            "AWSSDK.",
            "Amazon.",
            "Elasticsearch",
            "Microsoft.EntityFrameworkCore",
            "Minio",
            "MongoDB.",
            "Npgsql",
            "OpenSearch",
            "StackExchange.Redis"
        };

        var providerAssemblyReferences = ModuleProjectFiles()
            .Select(ProjectName)
            .SelectMany(module => Assembly.Load(new AssemblyName(module))
                .GetReferencedAssemblies()
                .Where(reference => providerPrefixes.Any(prefix =>
                    reference.Name?.StartsWith(prefix, StringComparison.Ordinal) == true))
                .Select(reference => $"{module}->{reference.Name}"));

        var providerPackageReferences = ModuleProjectFiles()
            .SelectMany(project => PackageReferences(project)
                .Where(package => providerPrefixes.Any(prefix =>
                    package.StartsWith(prefix, StringComparison.Ordinal)))
                .Select(package => $"{ProjectName(project)}->{package}"));

        Assert.Empty(providerAssemblyReferences.Concat(providerPackageReferences));
    }

    [Fact]
    public void SharedKernel_RemainsDependencyFreeAndMatchesMinimalPublicSurface()
    {
        var project = Path.Combine(SourceDirectory, "Zumbo.SharedKernel", "Zumbo.SharedKernel.csproj");
        Assert.Empty(ProjectReferences(project));

        var document = XDocument.Load(project);
        Assert.DoesNotContain(
            document.Descendants(),
            element => element.Name.LocalName == "PackageReference");

        var expectedPublicTypes = new[]
        {
            "Zumbo.SharedKernel.AggregateRoot",
            "Zumbo.SharedKernel.ApiError",
            "Zumbo.SharedKernel.ApiResponse`1",
            "Zumbo.SharedKernel.AuthenticationChallengeException",
            "Zumbo.SharedKernel.ConflictException",
            "Zumbo.SharedKernel.Entity",
            "Zumbo.SharedKernel.ForbiddenException",
            "Zumbo.SharedKernel.IClock",
            "Zumbo.SharedKernel.ICurrentUser",
            "Zumbo.SharedKernel.IDomainEvent",
            "Zumbo.SharedKernel.NotFoundException",
            "Zumbo.SharedKernel.UnauthorizedException",
            "Zumbo.SharedKernel.ValidationException",
            "Zumbo.SharedKernel.ValueObject",
            "Zumbo.SharedKernel.ZumboException"
        };

        AssertExactSet(
            expectedPublicTypes,
            typeof(Entity).Assembly.GetExportedTypes().Select(type => type.FullName).OfType<string>());
    }

    [Fact]
    public void DddPrimitives_AreUsedByRealModuleLifecyclesWithoutPersistenceCoupling()
    {
        var moduleTypes = ModuleProjectFiles()
            .Select(ProjectName)
            .Select(name => Assembly.Load(new AssemblyName(name)))
            .SelectMany(assembly => assembly.GetExportedTypes())
            .ToArray();

        var aggregateTypes = moduleTypes
            .Where(type => !type.IsAbstract && typeof(AggregateRoot).IsAssignableFrom(type))
            .ToArray();
        var valueObjectTypes = moduleTypes
            .Where(type => !type.IsAbstract && typeof(ValueObject).IsAssignableFrom(type))
            .ToArray();
        var domainEventTypes = moduleTypes
            .Where(type => !type.IsAbstract && typeof(IDomainEvent).IsAssignableFrom(type))
            .ToArray();
        var mapperTypes = moduleTypes
            .Where(type => type.GetInterfaces().Any(contract =>
                contract.IsGenericType
                && contract.GetGenericTypeDefinition() == typeof(IIntegrationEventMapper<,>)))
            .ToArray();

        AssertExpectedTypeNames(
            aggregateTypes,
            "ProjectMembershipAggregate",
            "WorkflowDefinitionAggregate",
            "WorkItemAggregate");
        AssertExpectedTypeNames(
            valueObjectTypes,
            "PreparedWorkItemTransition",
            "ProjectMemberRole",
            "SprintAssignment");
        AssertExpectedTypeNames(
            domainEventTypes,
            "ProjectMemberAddedDomainEvent",
            "ProjectMemberRemovedDomainEvent",
            "ProjectMemberRoleChangedDomainEvent",
            "WorkflowDefinedDomainEvent",
            "WorkItemMovedDomainEvent",
            "WorkItemPlanningChangedDomainEvent");
        AssertExpectedTypeNames(
            mapperTypes,
            "ProjectMembershipDomainEventMapper",
            "WorkflowDomainEventMapper",
            "WorkItemDomainEventMapper");

        Assert.All(aggregateTypes, type => Assert.False(typeof(IDocument).IsAssignableFrom(type)));
        Assert.DoesNotContain(
            typeof(Entity).Assembly.GetExportedTypes(),
            type => type.Name is "Error" or "Result" || type.Name.StartsWith("Result`", StringComparison.Ordinal));

        AssertModuleUsesAggregate("Zumbo.Modules.WorkItems", "WorkItemAggregate.Rehydrate");
        AssertModuleUsesAggregate("Zumbo.Modules.Workflows", "WorkflowDefinitionAggregate.Define");
        AssertModuleUsesAggregate("Zumbo.Modules.Projects", "ProjectMembershipAggregate.Rehydrate");
    }

    [Fact]
    public void ModuleProjectReferences_MatchExplicitAllowedContractGraph()
    {
        var expected = new[]
        {
            "Zumbo.Modules.Audit->Zumbo.BuildingBlocks.Application",
            "Zumbo.Modules.Audit->Zumbo.SharedKernel",
            "Zumbo.Modules.Boards->Zumbo.BuildingBlocks.Application",
            "Zumbo.Modules.Boards->Zumbo.SharedKernel",
            "Zumbo.Modules.Identity->Zumbo.BuildingBlocks.Application",
            "Zumbo.Modules.Identity->Zumbo.SharedKernel",
            "Zumbo.Modules.Notifications->Zumbo.BuildingBlocks.Application",
            "Zumbo.Modules.Notifications->Zumbo.SharedKernel",
            "Zumbo.Modules.Organizations->Zumbo.BuildingBlocks.Application",
            "Zumbo.Modules.Organizations->Zumbo.SharedKernel",
            "Zumbo.Modules.Projects->Zumbo.BuildingBlocks.Application",
            "Zumbo.Modules.Projects->Zumbo.SharedKernel",
            "Zumbo.Modules.Teams->Zumbo.BuildingBlocks.Application",
            "Zumbo.Modules.Teams->Zumbo.SharedKernel",
            "Zumbo.Modules.Workflows->Zumbo.BuildingBlocks.Application",
            "Zumbo.Modules.Workflows->Zumbo.SharedKernel",
            "Zumbo.Modules.WorkItems->Zumbo.BuildingBlocks.Application",
            "Zumbo.Modules.WorkItems->Zumbo.SharedKernel"
        };

        var actual = ModuleProjectFiles()
            .SelectMany(project => ProjectReferences(project)
                .Select(reference => $"{ProjectName(project)}->{reference}"));

        AssertExactSet(expected, actual);
    }

    [Fact]
    public void EveryModule_HasRepresentativeReadAndWriteVerticalSliceTypes()
    {
        var expectedTypes = new Dictionary<string, string[]>
        {
            ["Zumbo.Modules.Audit"] =
            [
                "AuditLogQuery", "QueryAuditLogValidator", "QueryAuditLogHandler", "AuditLogPageResponse",
                "WriteAuditLogCommand", "WriteAuditLogValidator", "WriteAuditLogHandler", "WriteAuditLogResponse"
            ],
            ["Zumbo.Modules.Boards"] =
            [
                "ListBoardsByProjectQuery", "ListBoardsByProjectValidator", "ListBoardsByProjectHandler", "BoardResponse",
                "CreateBoardRequest", "CreateBoardValidator", "CreateBoardHandler", "BoardResponse"
            ],
            ["Zumbo.Modules.Identity"] =
            [
                "SearchUsersQuery", "SearchUsersValidator", "SearchUsersHandler", "UserProfileResponse",
                "RegisterUserRequest", "RegisterUserValidator", "RegisterUserHandler", "AuthResponse"
            ],
            ["Zumbo.Modules.Notifications"] =
            [
                "ListNotificationsQuery", "ListNotificationsValidator", "ListNotificationsHandler", "NotificationResponse",
                "MarkNotificationAsReadCommand", "MarkNotificationAsReadValidator", "MarkNotificationAsReadHandler", "MarkNotificationAsReadResponse"
            ],
            ["Zumbo.Modules.Organizations"] =
            [
                "ListOrganizationsQuery", "ListOrganizationsValidator", "ListOrganizationsHandler", "OrganizationResponse",
                "CreateOrganizationRequest", "CreateOrganizationValidator", "CreateOrganizationHandler", "OrganizationResponse"
            ],
            ["Zumbo.Modules.Projects"] =
            [
                "ListProjectsQuery", "ListProjectsValidator", "ListProjectsHandler", "ProjectResponse",
                "CreateProjectRequest", "CreateProjectValidator", "CreateProjectHandler", "ProjectResponse"
            ],
            ["Zumbo.Modules.Teams"] =
            [
                "ListTeamsQuery", "ListTeamsValidator", "ListTeamsHandler", "TeamResponse",
                "CreateTeamRequest", "CreateTeamValidator", "CreateTeamHandler", "TeamResponse"
            ],
            ["Zumbo.Modules.Workflows"] =
            [
                "GetWorkflowQuery", "GetWorkflowValidator", "GetWorkflowHandler", "WorkflowResponse",
                "CreateWorkflowRequest", "UpsertWorkflowValidator", "UpsertWorkflowHandler", "WorkflowResponse"
            ],
            ["Zumbo.Modules.WorkItems"] =
            [
                "WorkItemSearchRequest", "SearchWorkItemsValidator", "SearchWorkItemsHandler", "WorkItemResponse",
                "CreateWorkItemRequest", "CreateWorkItemValidator", "CreateWorkItemHandler", "WorkItemResponse"
            ]
        };

        foreach (var (assemblyName, typeNames) in expectedTypes)
        {
            var exportedTypeNames = Assembly.Load(new AssemblyName(assemblyName))
                .GetExportedTypes()
                .Select(type => type.Name)
                .ToHashSet(StringComparer.Ordinal);

            Assert.All(typeNames, typeName => Assert.Contains(typeName, exportedTypeNames));
        }
    }

    [Fact]
    public void IdentityRegistrationAndUserSearch_ArePortFocusedVerticalSlicesWithCompatibilityFacades()
    {
        var identityDirectory = Path.Combine(SourceDirectory, "Zumbo.Modules.Identity");
        var registerDirectory = Path.Combine(identityDirectory, "Features", "RegisterUser");
        var searchDirectory = Path.Combine(identityDirectory, "Features", "SearchUsers");
        var representativeDirectory = Path.Combine(identityDirectory, "Features", "RepresentativeIdentitySlices");

        Assert.True(File.Exists(Path.Combine(registerDirectory, "RegisterUserRequest.cs")));
        Assert.True(File.Exists(Path.Combine(registerDirectory, "RegisterUserValidator.cs")));
        Assert.True(File.Exists(Path.Combine(registerDirectory, "RegisterUserHandler.cs")));
        Assert.True(File.Exists(Path.Combine(registerDirectory, "RegisterUserSlice.cs")));
        Assert.True(File.Exists(Path.Combine(searchDirectory, "SearchUsersQuery.cs")));
        Assert.True(File.Exists(Path.Combine(searchDirectory, "SearchUsersValidator.cs")));
        Assert.True(File.Exists(Path.Combine(searchDirectory, "SearchUsersHandler.cs")));
        Assert.True(File.Exists(Path.Combine(searchDirectory, "SearchUsersSlice.cs")));
        Assert.Empty(
            Directory.Exists(representativeDirectory)
                ? Directory.GetFiles(representativeDirectory, "*.cs", SearchOption.AllDirectories)
                : []);

        var registerSlice = File.ReadAllText(Path.Combine(registerDirectory, "RegisterUserSlice.cs"));
        var searchSlice = File.ReadAllText(Path.Combine(searchDirectory, "SearchUsersSlice.cs"));
        Assert.DoesNotContain("IdentityService", registerSlice, StringComparison.Ordinal);
        Assert.DoesNotContain("IdentityService", searchSlice, StringComparison.Ordinal);
        Assert.Contains("IUserRepository", registerSlice, StringComparison.Ordinal);
        Assert.Contains("IDurableTransactionRunner", registerSlice, StringComparison.Ordinal);
        Assert.Contains("IUserRepository", searchSlice, StringComparison.Ordinal);
        Assert.Contains("ICurrentUser", searchSlice, StringComparison.Ordinal);

        var registerFacade = File.ReadAllText(Path.Combine(
            identityDirectory,
            "Module",
            "Identity",
            "IdentityService",
            "IdentityService.RegisterAsync.cs"));
        var searchFacade = File.ReadAllText(Path.Combine(
            identityDirectory,
            "Module",
            "Identity",
            "IdentityService",
            "IdentityService.SearchUsersAsync.cs"));
        Assert.Contains("registerUserHandler.HandleAsync", registerFacade, StringComparison.Ordinal);
        Assert.Contains("searchUsersHandler.HandleAsync", searchFacade, StringComparison.Ordinal);

        var composition = File.ReadAllText(Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Endpoints",
            "IdentityEndpoints.cs"));
        Assert.Contains("AddScoped<RegisterUserHandler>(provider =>", composition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<SearchUsersHandler>(provider =>", composition, StringComparison.Ordinal);
    }

    [Fact]
    public void OrganizationCreateAndList_ArePortFocusedVerticalSlicesWithCompatibilityFacades()
    {
        var moduleDirectory = Path.Combine(SourceDirectory, "Zumbo.Modules.Organizations");
        var createDirectory = Path.Combine(moduleDirectory, "Features", "CreateOrganization");
        var listDirectory = Path.Combine(moduleDirectory, "Features", "ListOrganizations");
        var representativeDirectory = Path.Combine(
            moduleDirectory,
            "Features",
            "RepresentativeOrganizationSlices");

        Assert.True(File.Exists(Path.Combine(createDirectory, "CreateOrganizationRequest.cs")));
        Assert.True(File.Exists(Path.Combine(createDirectory, "CreateOrganizationValidator.cs")));
        Assert.True(File.Exists(Path.Combine(createDirectory, "CreateOrganizationHandler.cs")));
        Assert.True(File.Exists(Path.Combine(createDirectory, "CreateOrganizationSlice.cs")));
        Assert.True(File.Exists(Path.Combine(listDirectory, "ListOrganizationsQuery.cs")));
        Assert.True(File.Exists(Path.Combine(listDirectory, "ListOrganizationsValidator.cs")));
        Assert.True(File.Exists(Path.Combine(listDirectory, "ListOrganizationsHandler.cs")));
        Assert.True(File.Exists(Path.Combine(listDirectory, "ListOrganizationsSlice.cs")));
        Assert.Empty(
            Directory.Exists(representativeDirectory)
                ? Directory.GetFiles(representativeDirectory, "*.cs", SearchOption.AllDirectories)
                : []);

        var createSlice = File.ReadAllText(Path.Combine(createDirectory, "CreateOrganizationSlice.cs"));
        var listSlice = File.ReadAllText(Path.Combine(listDirectory, "ListOrganizationsSlice.cs"));
        Assert.DoesNotContain("OrganizationService", createSlice, StringComparison.Ordinal);
        Assert.DoesNotContain("OrganizationService", listSlice, StringComparison.Ordinal);
        Assert.Contains("IDocumentRepository<OrganizationDocument>", createSlice, StringComparison.Ordinal);
        Assert.Contains("IDistributedLockProvider", createSlice, StringComparison.Ordinal);
        Assert.Contains("IDocumentRepository<OrganizationDocument>", listSlice, StringComparison.Ordinal);
        Assert.Contains("ICurrentUser", listSlice, StringComparison.Ordinal);

        var facade = File.ReadAllText(Path.Combine(moduleDirectory, "OrganizationsModule.cs"));
        Assert.Contains("createOrganizationHandler.HandleAsync", facade, StringComparison.Ordinal);
        Assert.Contains("listOrganizationsHandler.HandleAsync", facade, StringComparison.Ordinal);

        var composition = File.ReadAllText(Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Endpoints",
            "OrganizationsEndpoints.cs"));
        Assert.Contains("AddScoped<CreateOrganizationHandler>(provider =>", composition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<ListOrganizationsHandler>(provider =>", composition, StringComparison.Ordinal);
    }

    [Fact]
    public void TeamCreateAndList_ArePortFocusedVerticalSlicesWithCompatibilityFacades()
    {
        var moduleDirectory = Path.Combine(SourceDirectory, "Zumbo.Modules.Teams");
        var createDirectory = Path.Combine(moduleDirectory, "Features", "CreateTeam");
        var listDirectory = Path.Combine(moduleDirectory, "Features", "ListTeams");
        var representativeDirectory = Path.Combine(moduleDirectory, "Features", "RepresentativeTeamSlices");

        Assert.True(File.Exists(Path.Combine(createDirectory, "CreateTeamRequest.cs")));
        Assert.True(File.Exists(Path.Combine(createDirectory, "CreateTeamValidator.cs")));
        Assert.True(File.Exists(Path.Combine(createDirectory, "CreateTeamHandler.cs")));
        Assert.True(File.Exists(Path.Combine(createDirectory, "CreateTeamSlice.cs")));
        Assert.True(File.Exists(Path.Combine(listDirectory, "ListTeamsQuery.cs")));
        Assert.True(File.Exists(Path.Combine(listDirectory, "ListTeamsValidator.cs")));
        Assert.True(File.Exists(Path.Combine(listDirectory, "ListTeamsHandler.cs")));
        Assert.True(File.Exists(Path.Combine(listDirectory, "ListTeamsSlice.cs")));
        Assert.Empty(
            Directory.Exists(representativeDirectory)
                ? Directory.GetFiles(representativeDirectory, "*.cs", SearchOption.AllDirectories)
                : []);

        var createSlice = File.ReadAllText(Path.Combine(createDirectory, "CreateTeamSlice.cs"));
        var listSlice = File.ReadAllText(Path.Combine(listDirectory, "ListTeamsSlice.cs"));
        Assert.DoesNotContain("TeamService", createSlice, StringComparison.Ordinal);
        Assert.DoesNotContain("TeamService", listSlice, StringComparison.Ordinal);
        Assert.Contains("IDocumentRepository<TeamDocument>", createSlice, StringComparison.Ordinal);
        Assert.Contains("ITeamUserDirectory", createSlice, StringComparison.Ordinal);
        Assert.Contains("ITeamOrganizationDirectory", createSlice, StringComparison.Ordinal);
        Assert.Contains("IDocumentRepository<TeamDocument>", listSlice, StringComparison.Ordinal);
        Assert.Contains("ICurrentUser", listSlice, StringComparison.Ordinal);

        var facade = File.ReadAllText(Path.Combine(moduleDirectory, "TeamsModule.cs"));
        Assert.Contains("createTeamHandler.HandleAsync", facade, StringComparison.Ordinal);
        Assert.Contains("listTeamsHandler.HandleAsync", facade, StringComparison.Ordinal);

        var composition = File.ReadAllText(Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Endpoints",
            "TeamsEndpoints.cs"));
        Assert.Contains("AddScoped<CreateTeamHandler>(provider =>", composition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<ListTeamsHandler>(provider =>", composition, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectCreateAndList_ArePortFocusedVerticalSlicesWithCompatibilityFacades()
    {
        var moduleDirectory = Path.Combine(SourceDirectory, "Zumbo.Modules.Projects");
        var createDirectory = Path.Combine(moduleDirectory, "Features", "CreateProject");
        var listDirectory = Path.Combine(moduleDirectory, "Features", "ListProjects");
        var representativeDirectory = Path.Combine(moduleDirectory, "Features", "RepresentativeProjectSlices");

        Assert.True(File.Exists(Path.Combine(createDirectory, "CreateProjectRequest.cs")));
        Assert.True(File.Exists(Path.Combine(createDirectory, "CreateProjectValidator.cs")));
        Assert.True(File.Exists(Path.Combine(createDirectory, "CreateProjectHandler.cs")));
        Assert.True(File.Exists(Path.Combine(createDirectory, "CreateProjectSlice.cs")));
        Assert.True(File.Exists(Path.Combine(listDirectory, "ListProjectsQuery.cs")));
        Assert.True(File.Exists(Path.Combine(listDirectory, "ListProjectsValidator.cs")));
        Assert.True(File.Exists(Path.Combine(listDirectory, "ListProjectsHandler.cs")));
        Assert.True(File.Exists(Path.Combine(listDirectory, "ListProjectsSlice.cs")));
        Assert.Empty(
            Directory.Exists(representativeDirectory)
                ? Directory.GetFiles(representativeDirectory, "*.cs", SearchOption.AllDirectories)
                : []);

        var createSlice = File.ReadAllText(Path.Combine(createDirectory, "CreateProjectSlice.cs"));
        var listSlice = File.ReadAllText(Path.Combine(listDirectory, "ListProjectsSlice.cs"));
        Assert.DoesNotContain("ProjectService", createSlice, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectService", listSlice, StringComparison.Ordinal);
        Assert.Contains("IDocumentRepository<ProjectDocument>", createSlice, StringComparison.Ordinal);
        Assert.Contains("IProjectMemberDirectory", createSlice, StringComparison.Ordinal);
        Assert.Contains("IProjectOrganizationDirectory", createSlice, StringComparison.Ordinal);
        Assert.Contains("IProjectAuditWriter", createSlice, StringComparison.Ordinal);
        Assert.Contains("IDocumentRepository<ProjectDocument>", listSlice, StringComparison.Ordinal);
        Assert.Contains("ICurrentUser", listSlice, StringComparison.Ordinal);

        var facade = File.ReadAllText(Path.Combine(moduleDirectory, "ProjectsModule.cs"));
        Assert.Contains("createProjectHandler.HandleAsync", facade, StringComparison.Ordinal);
        Assert.Contains("listProjectsHandler.HandleAsync", facade, StringComparison.Ordinal);

        var composition = File.ReadAllText(Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Endpoints",
            "ProjectsEndpoints.cs"));
        Assert.Contains("AddScoped<CreateProjectHandler>(provider =>", composition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<ListProjectsHandler>(provider =>", composition, StringComparison.Ordinal);
    }

    [Fact]
    public void BoardCreateAndProjectList_ArePortFocusedVerticalSlicesWithCompatibilityFacades()
    {
        var moduleDirectory = Path.Combine(SourceDirectory, "Zumbo.Modules.Boards");
        var createDirectory = Path.Combine(moduleDirectory, "Features", "CreateBoard");
        var listDirectory = Path.Combine(moduleDirectory, "Features", "ListBoardsByProject");
        var representativeDirectory = Path.Combine(moduleDirectory, "Features", "RepresentativeBoardSlices");

        Assert.True(File.Exists(Path.Combine(createDirectory, "CreateBoardRequest.cs")));
        Assert.True(File.Exists(Path.Combine(createDirectory, "CreateBoardValidator.cs")));
        Assert.True(File.Exists(Path.Combine(createDirectory, "CreateBoardHandler.cs")));
        Assert.True(File.Exists(Path.Combine(createDirectory, "CreateBoardSlice.cs")));
        Assert.True(File.Exists(Path.Combine(listDirectory, "ListBoardsByProjectQuery.cs")));
        Assert.True(File.Exists(Path.Combine(listDirectory, "ListBoardsByProjectValidator.cs")));
        Assert.True(File.Exists(Path.Combine(listDirectory, "ListBoardsByProjectHandler.cs")));
        Assert.True(File.Exists(Path.Combine(listDirectory, "ListBoardsByProjectSlice.cs")));
        Assert.Empty(
            Directory.Exists(representativeDirectory)
                ? Directory.GetFiles(representativeDirectory, "*.cs", SearchOption.AllDirectories)
                : []);

        var createSlice = File.ReadAllText(Path.Combine(createDirectory, "CreateBoardSlice.cs"));
        var listSlice = File.ReadAllText(Path.Combine(listDirectory, "ListBoardsByProjectSlice.cs"));
        Assert.DoesNotContain("BoardService", createSlice, StringComparison.Ordinal);
        Assert.DoesNotContain("BoardService", listSlice, StringComparison.Ordinal);
        Assert.Contains("IDocumentRepository<BoardDocument>", createSlice, StringComparison.Ordinal);
        Assert.Contains("IBoardProjectAccessChecker", createSlice, StringComparison.Ordinal);
        Assert.Contains("IDistributedLockProvider", createSlice, StringComparison.Ordinal);
        Assert.Contains("IBoardAuditWriter", createSlice, StringComparison.Ordinal);
        Assert.Contains("IDocumentRepository<BoardDocument>", listSlice, StringComparison.Ordinal);
        Assert.Contains("ICurrentUser", listSlice, StringComparison.Ordinal);

        var createFacade = File.ReadAllText(Path.Combine(
            moduleDirectory,
            "Module",
            "Boards",
            "BoardService",
            "BoardService.CreateAsync.cs"));
        var listFacade = File.ReadAllText(Path.Combine(
            moduleDirectory,
            "Module",
            "Boards",
            "BoardService",
            "BoardService.ListByProjectAsync.cs"));
        Assert.Contains("createBoardHandler.HandleAsync", createFacade, StringComparison.Ordinal);
        Assert.Contains("listBoardsByProjectHandler.HandleAsync", listFacade, StringComparison.Ordinal);

        var composition = File.ReadAllText(Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Endpoints",
            "BoardsEndpoints.cs"));
        Assert.Contains("AddScoped<CreateBoardHandler>(provider =>", composition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<ListBoardsByProjectHandler>(provider =>", composition, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkflowUpsertAndRead_ArePortFocusedVerticalSlicesWithCompatibilityFacades()
    {
        var moduleDirectory = Path.Combine(SourceDirectory, "Zumbo.Modules.Workflows");
        var upsertDirectory = Path.Combine(moduleDirectory, "Features", "UpsertWorkflow");
        var getDirectory = Path.Combine(moduleDirectory, "Features", "GetWorkflow");
        var representativeDirectory = Path.Combine(moduleDirectory, "Features", "RepresentativeWorkflowSlices");

        Assert.True(File.Exists(Path.Combine(upsertDirectory, "CreateWorkflowRequest.cs")));
        Assert.True(File.Exists(Path.Combine(upsertDirectory, "UpsertWorkflowValidator.cs")));
        Assert.True(File.Exists(Path.Combine(upsertDirectory, "UpsertWorkflowHandler.cs")));
        Assert.True(File.Exists(Path.Combine(upsertDirectory, "UpsertWorkflowSlice.cs")));
        Assert.True(File.Exists(Path.Combine(getDirectory, "GetWorkflowQuery.cs")));
        Assert.True(File.Exists(Path.Combine(getDirectory, "GetWorkflowValidator.cs")));
        Assert.True(File.Exists(Path.Combine(getDirectory, "GetWorkflowHandler.cs")));
        Assert.True(File.Exists(Path.Combine(getDirectory, "GetWorkflowSlice.cs")));
        Assert.Empty(
            Directory.Exists(representativeDirectory)
                ? Directory.GetFiles(representativeDirectory, "*.cs", SearchOption.AllDirectories)
                : []);

        var upsertSlice = File.ReadAllText(Path.Combine(upsertDirectory, "UpsertWorkflowSlice.cs"));
        var getSlice = File.ReadAllText(Path.Combine(getDirectory, "GetWorkflowSlice.cs"));
        Assert.DoesNotContain("WorkflowService", upsertSlice, StringComparison.Ordinal);
        Assert.DoesNotContain("WorkflowService", getSlice, StringComparison.Ordinal);
        Assert.Contains("IDocumentRepository<WorkflowDefinitionDocument>", upsertSlice, StringComparison.Ordinal);
        Assert.Contains("IDistributedLockProvider", upsertSlice, StringComparison.Ordinal);
        Assert.Contains("IWorkflowAuditWriter", upsertSlice, StringComparison.Ordinal);
        Assert.Contains("IWorkflowPublicationGuard", upsertSlice, StringComparison.Ordinal);
        Assert.Contains("IDocumentRepository<WorkflowDefinitionDocument>", getSlice, StringComparison.Ordinal);
        Assert.Contains("IWorkflowProjectAccessChecker", getSlice, StringComparison.Ordinal);

        var facade = File.ReadAllText(Path.Combine(
            moduleDirectory,
            "Module",
            "Workflows",
            "WorkflowService.cs"));
        Assert.Contains("upsertWorkflowHandler.HandleAsync", facade, StringComparison.Ordinal);
        Assert.Contains("getWorkflowHandler.HandleAsync", facade, StringComparison.Ordinal);

        var composition = File.ReadAllText(Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Endpoints",
            "WorkflowEndpoints.cs"));
        Assert.Contains("AddScoped<UpsertWorkflowHandler>(provider =>", composition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<GetWorkflowHandler>(provider =>", composition, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkItemCreateAndSearch_ArePortFocusedVerticalSlicesWithCompatibilityFacades()
    {
        var moduleDirectory = Path.Combine(SourceDirectory, "Zumbo.Modules.WorkItems");
        var createDirectory = Path.Combine(moduleDirectory, "Features", "CreateWorkItem");
        var searchDirectory = Path.Combine(moduleDirectory, "Features", "SearchWorkItems");
        var representativeDirectory = Path.Combine(
            moduleDirectory,
            "Features",
            "RepresentativeWorkItemSlices");

        Assert.True(File.Exists(Path.Combine(createDirectory, "CreateWorkItemRequest.cs")));
        Assert.True(File.Exists(Path.Combine(createDirectory, "CreateWorkItemValidator.cs")));
        Assert.True(File.Exists(Path.Combine(createDirectory, "CreateWorkItemHandler.cs")));
        Assert.True(File.Exists(Path.Combine(createDirectory, "CreateWorkItemSlice.cs")));
        Assert.True(File.Exists(Path.Combine(searchDirectory, "WorkItemSearchRequest.cs")));
        Assert.True(File.Exists(Path.Combine(searchDirectory, "SearchWorkItemsValidator.cs")));
        Assert.True(File.Exists(Path.Combine(searchDirectory, "SearchWorkItemsHandler.cs")));
        Assert.True(File.Exists(Path.Combine(searchDirectory, "SearchWorkItemsSlice.cs")));
        Assert.Empty(
            Directory.Exists(representativeDirectory)
                ? Directory.GetFiles(representativeDirectory, "*.cs", SearchOption.AllDirectories)
                : []);

        var createSlice = File.ReadAllText(Path.Combine(createDirectory, "CreateWorkItemSlice.cs"));
        var searchSlice = File.ReadAllText(Path.Combine(searchDirectory, "SearchWorkItemsSlice.cs"));
        Assert.DoesNotContain("WorkItemService", createSlice, StringComparison.Ordinal);
        Assert.DoesNotContain("WorkItemService", searchSlice, StringComparison.Ordinal);
        Assert.Contains("IDocumentRepository<WorkItemDocument>", createSlice, StringComparison.Ordinal);
        Assert.Contains("IProjectPermissionChecker", createSlice, StringComparison.Ordinal);
        Assert.Contains("IDistributedLockProvider", createSlice, StringComparison.Ordinal);
        Assert.Contains("IWorkItemSearchPublisher", createSlice, StringComparison.Ordinal);
        Assert.Contains("IWorkItemActivityStore", createSlice, StringComparison.Ordinal);
        Assert.Contains("IDocumentRepository<WorkItemDocument>", searchSlice, StringComparison.Ordinal);
        Assert.Contains("IProjectPermissionChecker", searchSlice, StringComparison.Ordinal);
        Assert.Contains("IWorkItemSearchIndex", searchSlice, StringComparison.Ordinal);

        var createFacade = File.ReadAllText(Path.Combine(
            moduleDirectory,
            "Features",
            "Create",
            "WorkItemService.Create.cs"));
        var searchFacade = File.ReadAllText(Path.Combine(
            moduleDirectory,
            "Features",
            "Read",
            "WorkItemService.Read.cs"));
        Assert.Contains("createWorkItemHandler.HandleAsync", createFacade, StringComparison.Ordinal);
        Assert.Contains("searchWorkItemsHandler.HandleAsync", searchFacade, StringComparison.Ordinal);

        var composition = File.ReadAllText(Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Endpoints",
            "WorkItemEndpoints",
            "WorkItemEndpoints.AddWorkItemsModule.cs"));
        Assert.Contains("AddScoped<CreateWorkItemHandler>(provider =>", composition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<SearchWorkItemsHandler>(provider =>", composition, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionSourceFiles_StayWithinReadabilityLimit()
    {
        const int maximumLines = 500;
        var oversizedFiles = ProductionSourceFiles()
            .Select(path => new
            {
                Path = Path.GetRelativePath(SourceDirectory, path),
                Lines = File.ReadLines(path).Count()
            })
            .Where(file => file.Lines > maximumLines)
            .OrderByDescending(file => file.Lines)
            .ToArray();

        Assert.True(
            oversizedFiles.Length == 0,
            $"Production source files must not exceed {maximumLines} lines:{Environment.NewLine}"
                + string.Join(Environment.NewLine, oversizedFiles.Select(file => $"{file.Path}: {file.Lines}")));
    }

    [Fact]
    public void ProductionSourceFiles_ContainAtMostOneTopLevelType()
    {
        var offenders = ProductionSourceFiles()
            .Select(path => new
            {
                Path = Path.GetRelativePath(SourceDirectory, path),
                Count = TopLevelTypeCount(path)
            })
            .Where(file => file.Count > 1)
            .OrderBy(file => file.Path, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "Production source files must contain at most one top-level type:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, offenders.Select(file => $"{file.Path}: {file.Count}")));
    }

    [Fact]
    public void WorkItemGraphTraversal_UsesBoundedIndexedRepositoryOperations()
    {
        var path = Path.Combine(
            SourceDirectory,
            "Zumbo.Modules.WorkItems",
            "Services",
            "WorkItemGraph",
            "WorkItemGraphService.cs");
        var source = File.ReadAllText(path);

        Assert.Contains("MaxTraversalDepth", source, StringComparison.Ordinal);
        Assert.Contains("MaxVisitedNodes", source, StringComparison.Ordinal);
        Assert.Contains("CountByFilterAsync", source, StringComparison.Ordinal);
        Assert.Contains("ListByFilterAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ListByCursorAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadActiveProjectItemsAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NotificationExtractionManifest_MatchesCurrentCodeBoundaries()
    {
        var manifestPath = Path.Combine(BackendDirectory, "extraction", "notifications.v1.json");
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = manifest.RootElement;

        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("OPS-007", root.GetProperty("task").GetString());
        Assert.Equal("Notifications", root.GetProperty("selectedModule").GetString());
        Assert.False(root.GetProperty("productionSplit").GetBoolean());
        Assert.Equal(
            ["team.invitation-notification.v1", "work-item.notification.v1"],
            root.GetProperty("consumedEvents")
                .EnumerateArray()
                .Select(item => item.GetProperty("eventType").GetString())
                .Order(StringComparer.Ordinal));
        Assert.All(root.GetProperty("consumedEvents").EnumerateArray(), item =>
            Assert.Equal(1, item.GetProperty("schemaVersion").GetInt32()));

        var notificationProject = Path.Combine(
            SourceDirectory,
            "Zumbo.Modules.Notifications",
            "Zumbo.Modules.Notifications.csproj");
        AssertExactSet(
            ["Zumbo.BuildingBlocks.Application", "Zumbo.SharedKernel"],
            ProjectReferences(notificationProject));
        Assert.Empty(PackageReferences(notificationProject));

        Assert.Equal("Zumbo.Modules.Teams", typeof(Zumbo.Modules.Teams.TeamInvitationNotificationEvent).Assembly.GetName().Name);
        Assert.Equal("Zumbo.Modules.WorkItems", typeof(WorkItemNotificationEvent).Assembly.GetName().Name);
        Assert.Equal("team.invitation-notification.v1", Zumbo.Modules.Teams.TeamDurableEventTypes.InvitationNotification);
        Assert.Equal("work-item.notification.v1", WorkItemDurableEventTypes.Notification);

        var notificationAdapters = ReadSourceScope(
            Path.Combine(SourceDirectory, "Zumbo.Api", "NotificationAdapters"));
        var notificationHost = File.ReadAllText(Path.Combine(SourceDirectory, "Zumbo.Api", "Endpoints", "NotificationEndpoints.cs"));
        var workItemHost = ReadSourceScope(
            Path.Combine(SourceDirectory, "Zumbo.Api", "Endpoints", "WorkItemEndpoints"));
        var gateway = ReadSourceScope(Path.Combine(SourceDirectory, "Zumbo.Gateway", "GatewayHost"));
        Assert.DoesNotContain("Zumbo.Modules.WorkItems", notificationAdapters, StringComparison.Ordinal);
        Assert.DoesNotContain("DueDateReminderHostedService", notificationHost, StringComparison.Ordinal);
        Assert.Contains("DueDateReminderHostedService", workItemHost, StringComparison.Ordinal);
        Assert.Contains("/api/notifications/{**catch-all}", gateway, StringComparison.Ordinal);
        Assert.Contains("NotificationExtractionEnabled", gateway, StringComparison.Ordinal);
        Assert.Contains("Order = -100", gateway, StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> ModuleProjectFiles() =>
        Directory.GetDirectories(SourceDirectory, "Zumbo.Modules.*")
            .SelectMany(directory => Directory.GetFiles(directory, "*.csproj"))
            .Order(StringComparer.Ordinal)
            .ToList();

    private static IReadOnlyList<string> ProjectReferences(string projectFile) =>
        XDocument.Load(projectFile)
            .Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .OfType<string>()
            .Select(reference => Path.GetFileNameWithoutExtension(reference.Replace('\\', '/')))
            .Order(StringComparer.Ordinal)
            .ToList();

    private static IReadOnlyList<string> PackageReferences(string projectFile) =>
        XDocument.Load(projectFile)
            .Descendants()
            .Where(element => element.Name.LocalName == "PackageReference")
            .Select(element => element.Attribute("Include")?.Value)
            .OfType<string>()
            .Order(StringComparer.Ordinal)
            .ToList();

    private static string ProjectName(string projectFile) => Path.GetFileNameWithoutExtension(projectFile);

    private static void AssertExpectedTypeNames(IEnumerable<Type> actualTypes, params string[] expectedNames)
    {
        var actualNames = actualTypes.Select(type => type.Name).ToHashSet(StringComparer.Ordinal);
        Assert.All(expectedNames, expectedName => Assert.Contains(expectedName, actualNames));
    }

    private static IReadOnlyList<string> ProductionSourceFiles() =>
        Directory.GetFiles(SourceDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .ToList();

    private static int TopLevelTypeCount(string path)
    {
        var root = CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetCompilationUnitRoot();
        return NamespaceMembers(root.Members)
            .Count(member => member is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax);
    }

    private static IEnumerable<MemberDeclarationSyntax> NamespaceMembers(
        IEnumerable<MemberDeclarationSyntax> members)
    {
        foreach (var member in members)
        {
            if (member is BaseNamespaceDeclarationSyntax namespaceDeclaration)
            {
                foreach (var namespaceMember in NamespaceMembers(namespaceDeclaration.Members))
                {
                    yield return namespaceMember;
                }

                continue;
            }

            yield return member;
        }
    }

    private static string ReadSourceScope(string directory) =>
        string.Join(
            Environment.NewLine,
            Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Contains(
                    $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.Ordinal)
                .Select(File.ReadAllText));

    private static void AssertModuleUsesAggregate(string moduleName, string invocation)
    {
        var source = ReadSourceScope(Path.Combine(SourceDirectory, moduleName));
        Assert.Contains(invocation, source, StringComparison.Ordinal);
    }

    private static void AssertExactSet(IEnumerable<string> expected, IEnumerable<string> actual) =>
        Assert.Equal(
            expected.Order(StringComparer.Ordinal),
            actual.Order(StringComparer.Ordinal));
}
