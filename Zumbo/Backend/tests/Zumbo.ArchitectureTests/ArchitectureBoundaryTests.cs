using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
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
    public void ApiHostBusinessLogicFiles_MatchExactMigrationAllowList()
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

        AssertExactSet(
        [
            "Endpoints/AuditEndpoints.cs",
            "Endpoints/AutomationEndpoints.cs",
            "Endpoints/BoardsEndpoints.cs",
            "Endpoints/CapacityPlanningEndpoints.cs",
            "Endpoints/DashboardEndpoints.cs",
            "Endpoints/DevelopmentIntegrationEndpoints.cs",
            "Endpoints/GoalEndpoints.cs",
            "Endpoints/IdentityEndpoints.cs",
            "Endpoints/IntakeEndpoints.cs",
            "Endpoints/KnowledgeEndpoints.cs",
            "Endpoints/NotificationEndpoints.cs",
            "Endpoints/OperationsEndpoints.cs",
            "Endpoints/OrganizationsEndpoints.cs",
            "Endpoints/PortfolioEndpoints.cs",
            "Endpoints/ProjectsEndpoints.cs",
            "Endpoints/SprintEndpoints.cs",
            "Endpoints/TeamsEndpoints.cs",
            "Endpoints/WebhookEndpoints.cs",
            "Endpoints/WorkflowEndpoints.cs",
            "Endpoints/WorkItemEndpoints.cs",
            "Endpoints/WorkItemTypeSchemaEndpoints.cs",
            "Hosting/ApiPipeline.cs"
        ], actual);
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

        AssertServiceUsesAggregate("Zumbo.Modules.WorkItems", "WorkItemsModule.cs", "WorkItemAggregate.Rehydrate");
        AssertServiceUsesAggregate("Zumbo.Modules.Workflows", "WorkflowsModule.cs", "WorkflowDefinitionAggregate.Define");
        AssertServiceUsesAggregate("Zumbo.Modules.Projects", "ProjectMembership.cs", "ProjectMembershipAggregate.Rehydrate");
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
    public void LargeModuleFiles_StayBelowReducedMigrationCeilings()
    {
        var reducedCeilings = new Dictionary<string, int>
        {
            ["Zumbo.Modules.Audit/AuditModule.cs"] = 139,
            ["Zumbo.Modules.Boards/BoardsModule.cs"] = 732,
            ["Zumbo.Modules.Identity/IdentityModule.cs"] = 863,
            ["Zumbo.Modules.Notifications/NotificationsModule.cs"] = 312,
            ["Zumbo.Modules.Organizations/OrganizationsModule.cs"] = 458,
            ["Zumbo.Modules.Projects/ProjectsModule.cs"] = 452,
            ["Zumbo.Modules.Teams/TeamsModule.cs"] = 520,
            ["Zumbo.Modules.Workflows/WorkflowsModule.cs"] = 370,
            ["Zumbo.Modules.WorkItems/WorkItemsModule.cs"] = 2183
        };

        foreach (var (relativePath, maximumLines) in reducedCeilings)
        {
            var path = Path.Combine(SourceDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
            var actualLines = File.ReadLines(path).Count();

            Assert.True(
                actualLines <= maximumLines,
                $"{relativePath} has {actualLines} lines; migration ceiling is {maximumLines}.");
        }
    }

    [Fact]
    public void WorkItemGraphTraversal_UsesBoundedIndexedRepositoryOperations()
    {
        var path = Path.Combine(SourceDirectory, "Zumbo.Modules.WorkItems", "WorkItemGraphService.cs");
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

        var notificationAdapters = File.ReadAllText(Path.Combine(SourceDirectory, "Zumbo.Api", "NotificationAdapters.cs"));
        var notificationHost = File.ReadAllText(Path.Combine(SourceDirectory, "Zumbo.Api", "Endpoints", "NotificationEndpoints.cs"));
        var workItemHost = File.ReadAllText(Path.Combine(SourceDirectory, "Zumbo.Api", "Endpoints", "WorkItemEndpoints.cs"));
        var gateway = File.ReadAllText(Path.Combine(SourceDirectory, "Zumbo.Gateway", "GatewayHost.cs"));
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

    private static void AssertServiceUsesAggregate(string moduleName, string fileName, string invocation)
    {
        var path = Path.Combine(SourceDirectory, moduleName, fileName);
        Assert.Contains(invocation, File.ReadAllText(path), StringComparison.Ordinal);
    }

    private static void AssertExactSet(IEnumerable<string> expected, IEnumerable<string> actual) =>
        Assert.Equal(
            expected.Order(StringComparer.Ordinal),
            actual.Order(StringComparer.Ordinal));
}
