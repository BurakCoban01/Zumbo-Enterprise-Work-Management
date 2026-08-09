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
                relativePath.StartsWith("Presentation/Endpoints/", StringComparison.Ordinal)
                    || relativePath == "Composition/Hosting/ApiPipeline.cs",
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
    public void GatewaySource_RemainsProviderAndModuleAgnostic()
    {
        var gatewayDirectory = Path.Combine(SourceDirectory, "Zumbo.Gateway");
        var source = ReadSourceScope(gatewayDirectory);
        var forbiddenMarkers = new[]
        {
            "Zumbo.Modules.", "Zumbo.Api", "Npgsql", "MongoDB.",
            "StackExchange.Redis", "OpenSearch", "Minio"
        };

        Assert.All(forbiddenMarkers, marker =>
            Assert.DoesNotContain(marker, source, StringComparison.Ordinal));
    }

    [Fact]
    public void PostgreSqlPersistence_DependsOnlyOnApplicationPortsAndProviderPackage()
    {
        var persistenceDirectory = Path.Combine(SourceDirectory, "Zumbo.Persistence.PostgreSql");
        var project = Path.Combine(persistenceDirectory, "Zumbo.Persistence.PostgreSql.csproj");
        var source = ReadSourceScope(persistenceDirectory);

        AssertExactSet(["Zumbo.BuildingBlocks.Application"], ProjectReferences(project));
        AssertExactSet(["Npgsql"], PackageReferences(project));
        Assert.DoesNotContain("Zumbo.Modules.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Zumbo.Api", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MongoDB.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StackExchange.Redis", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PostgreSqlMigrations_AreOrderedDefinitionTypesWithCompatibilityRunner()
    {
        var persistenceDirectory = Path.Combine(SourceDirectory, "Zumbo.Persistence.PostgreSql");
        var migrationsDirectory = Path.Combine(
            persistenceDirectory,
            "Infrastructure",
            "Persistence",
            "Migrations");
        var definitionsDirectory = Path.Combine(migrationsDirectory, "Definitions");
        var definitionFiles = Directory.GetFiles(
                definitionsDirectory,
                "V*.cs",
                SearchOption.TopDirectoryOnly)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(37, definitionFiles.Length);
        Assert.Equal(
            Enumerable.Range(1, 37).Select(version => $"V{version:000}").ToArray(),
            definitionFiles.Select(path => (Path.GetFileName(path)!)[..4]).ToArray());
        Assert.All(definitionFiles, path =>
        {
            Assert.True(path.Length <= 225, $"Migration definition path exceeds budget: {path}");
            Assert.Contains(
                "PostgreSqlMigrationDefinition",
                File.ReadAllText(path),
                StringComparison.Ordinal);
        });

        var abstractionsDirectory = Path.Combine(migrationsDirectory, "Abstractions");
        Assert.Equal(
            [
                "IPostgreSqlMigrationRunner.cs",
                "PostgreSqlMigrationDefinition.cs",
                "PostgreSqlMigrationInfo.cs",
                "PostgreSqlMigrationStatus.cs"
            ],
            Directory.GetFiles(abstractionsDirectory, "*.cs", SearchOption.TopDirectoryOnly)
                .Select(path => Path.GetFileName(path)!)
                .Order(StringComparer.Ordinal)
                .ToArray());
        var abstraction = File.ReadAllText(Path.Combine(
            abstractionsDirectory,
            "PostgreSqlMigrationDefinition.cs"));
        Assert.Contains("long Version", abstraction, StringComparison.Ordinal);

        var runnerDirectory = Path.Combine(migrationsDirectory, "Runner");
        Assert.Equal(
            [
                "PostgreSqlMigrationRunner.Apply.cs",
                "PostgreSqlMigrationRunner.Inspection.cs",
                "PostgreSqlMigrationRunner.Ledger.cs",
                "PostgreSqlMigrationRunner.Locking.cs",
                "PostgreSqlMigrationRunner.Registry.cs",
                "PostgreSqlMigrationRunner.Rollback.cs",
                "PostgreSqlMigrationRunner.Sql.cs",
                "PostgreSqlMigrationRunner.cs"
            ],
            Directory.GetFiles(runnerDirectory, "*.cs", SearchOption.TopDirectoryOnly)
                .Select(path => Path.GetFileName(path)!)
                .Order(StringComparer.Ordinal)
                .ToArray());
        Assert.All(
            Directory.GetFiles(migrationsDirectory, "*.cs", SearchOption.AllDirectories),
            path => Assert.True(path.Length <= 225, $"Migration path exceeds budget: {path}"));

        var legacyMigrations = Path.Combine(
            persistenceDirectory,
            "PostgreSqlMigrations");
        Assert.Empty(
            Directory.Exists(legacyMigrations)
                ? Directory.GetFiles(legacyMigrations, "*.cs", SearchOption.AllDirectories)
                : []);

        var registryPath = Path.Combine(
            runnerDirectory,
            "PostgreSqlMigrationRunner.Registry.cs");
        Assert.Equal(
            37,
            File.ReadLines(registryPath).Count(line => line.TrimStart().StartsWith(
                "Migration.Create(",
                StringComparison.Ordinal)));
    }

    [Fact]
    public void MongoMigrations_AreGroupedByAdapterResponsibility()
    {
        var apiDirectory = Path.Combine(SourceDirectory, "Zumbo.Api");
        var migrationsDirectory = Path.Combine(
            apiDirectory,
            "Infrastructure",
            "Persistence",
            "MongoDb",
            "Migrations");

        Assert.Equal(
            [
                "IMongoMigrationExecutionContext.cs",
                "MongoIndexSpecification.cs",
                "MongoMigrationOptions.cs",
                "MongoMigrationOutcome.cs",
                "MongoMigrationRunReport.cs",
                "MongoMigrationStates.cs"
            ],
            FileNames(Path.Combine(migrationsDirectory, "Abstractions")));
        Assert.Equal(
            [
                "MongoMigrationLedgerDocument.cs",
                "MongoRankMigrationBackupDocument.cs"
            ],
            FileNames(Path.Combine(migrationsDirectory, "Documents")));
        Assert.Equal(25, FileNames(Path.Combine(
            migrationsDirectory,
            "Definitions",
            "Indexes")).Length);
        Assert.Equal(
            [
                "MongoApiKeyVersionBackfill.cs",
                "MongoLegacyMarkerCleanup.cs",
                "MongoMigrationRunner.ApiKeyVersionBackfill.cs",
                "MongoMigrationRunner.LegacyMarkerCleanup.cs",
                "MongoMigrationRunner.OrganizationBackfill.cs",
                "MongoMigrationRunner.ProjectLifecycleBackfill.cs",
                "MongoMigrationRunner.RankBackfill.cs",
                "MongoMigrationRunner.RefreshSessionBackfill.cs",
                "MongoMigrationRunner.SprintLifecycleBackfill.cs",
                "MongoMigrationRunner.TeamInviteBackfill.cs",
                "MongoMigrationRunner.TypeSchemaBackfill.cs",
                "MongoMigrationRunner.UserVersionBackfill.cs",
                "MongoMigrationRunner.WorkItemActivityBackfill.cs",
                "MongoMigrationRunner.WorkItemGraphBackfill.cs",
                "MongoMigrationRunner.WorkflowLifecycleBackfill.cs",
                "MongoOrganizationVersionBackfill.cs",
                "MongoProjectLifecycleBackfill.cs",
                "MongoRankBackfill.cs",
                "MongoRefreshSessionBackfill.cs",
                "MongoSprintLifecycleBackfill.cs",
                "MongoTeamInviteBackfill.cs",
                "MongoUserVersionBackfill.cs",
                "MongoWorkItemActivityBackfill.cs",
                "MongoWorkItemGraphBackfill.cs",
                "MongoWorkItemTypeSchemaBackfill.cs",
                "MongoWorkflowLifecycleBackfill.cs"
            ],
            FileNames(Path.Combine(migrationsDirectory, "Definitions", "Backfills")));
        Assert.Equal(
            [
                "MongoMigrationRunner.Bson.cs",
                "MongoMigrationRunner.Context.cs",
                "MongoMigrationRunner.Indexes.cs",
                "MongoMigrationRunner.Ledger.cs",
                "MongoMigrationRunner.Rollback.cs",
                "MongoMigrationRunner.cs"
            ],
            FileNames(Path.Combine(migrationsDirectory, "Runner")));

        Assert.All(
            Directory.GetFiles(migrationsDirectory, "*.cs", SearchOption.AllDirectories),
            path => Assert.True(path.Length <= 225, $"Mongo migration path exceeds budget: {path}"));
        var legacyMigrations = Path.Combine(apiDirectory, "MongoMigrations");
        Assert.Empty(
            Directory.Exists(legacyMigrations)
                ? Directory.GetFiles(legacyMigrations, "*.cs", SearchOption.AllDirectories)
                : []);

        var runnerPath = Path.Combine(
            migrationsDirectory,
            "Runner",
            "MongoMigrationRunner.cs");
        Assert.Equal(
            38,
            File.ReadLines(runnerPath).Count(line => line.TrimStart().StartsWith(
                "public const string ",
                StringComparison.Ordinal)));
    }

    [Fact]
    public void ApiPipeline_PreservesExactMiddlewareOrder()
    {
        var pipeline = Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Composition",
            "Hosting",
            "ApiPipeline.cs");
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
            ["WorkItemTypeSchemaEndpoints.cs"] = "Zumbo.Modules.WorkItems"
        };
        var endpointDirectory = Path.Combine(SourceDirectory, "Zumbo.Api", "Presentation", "Endpoints");

        AssertExactSet(
            expected.Keys,
            Directory.GetFiles(endpointDirectory, "*Endpoints.cs", SearchOption.AllDirectories)
                .Select(Path.GetFileName)
                .OfType<string>()
                .Where(fileName => !fileName.StartsWith("WorkItemEndpoints.", StringComparison.Ordinal)));
        foreach (var (fileName, owningModule) in expected)
        {
            var moduleUsings = File.ReadLines(EndpointHostPath(fileName))
                .Select(line => line.Trim())
                .Where(line => line.StartsWith("using Zumbo.Modules.", StringComparison.Ordinal))
                .Select(line => line["using ".Length..^1])
                .Select(moduleNamespace => string.Join('.', moduleNamespace.Split('.').Take(3)))
                .Distinct(StringComparer.Ordinal);

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
        var registerDirectory = Path.Combine(identityDirectory, "Application", "Features", "Registration");
        var searchDirectory = Path.Combine(identityDirectory, "Application", "Features", "UserSearch");
        var representativeDirectory = Path.Combine(
            identityDirectory,
            "Application",
            "Features",
            "RepresentativeIdentitySlices");

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

        var compatibilityDirectory = Path.Combine(identityDirectory, "Application", "Compatibility");
        var registerFacade = File.ReadAllText(Path.Combine(
            compatibilityDirectory,
            "IdentityService.Authentication.cs"));
        var searchFacade = File.ReadAllText(Path.Combine(
            compatibilityDirectory,
            "IdentityService.Account.cs"));
        Assert.Contains("registerUserHandler.HandleAsync", registerFacade, StringComparison.Ordinal);
        Assert.Contains("searchUsersHandler.HandleAsync", searchFacade, StringComparison.Ordinal);

        var composition = File.ReadAllText(EndpointHostPath("IdentityEndpoints.cs"));
        Assert.Contains("AddScoped<RegisterUserHandler>(provider =>", composition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<SearchUsersHandler>(provider =>", composition, StringComparison.Ordinal);
    }

    [Fact]
    public void OrganizationCreateAndList_ArePortFocusedVerticalSlicesWithCompatibilityFacades()
    {
        var moduleDirectory = Path.Combine(SourceDirectory, "Zumbo.Modules.Organizations");
        var createDirectory = Path.Combine(moduleDirectory, "Application", "Features", "OrganizationsCore");
        var listDirectory = createDirectory;
        var representativeDirectory = Path.Combine(
            moduleDirectory,
            "Application",
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

        var facade = File.ReadAllText(Path.Combine(
            moduleDirectory,
            "Application",
            "Compatibility",
            "OrganizationService.cs"));
        Assert.Contains("createOrganizationHandler.HandleAsync", facade, StringComparison.Ordinal);
        Assert.Contains("listOrganizationsHandler.HandleAsync", facade, StringComparison.Ordinal);

        var endpointHost = File.ReadAllText(EndpointHostPath("OrganizationsEndpoints.cs"));
        Assert.Contains("services.AddOrganizationServices()", endpointHost, StringComparison.Ordinal);
        Assert.DoesNotContain("IDocumentRepository", endpointHost, StringComparison.Ordinal);
        Assert.DoesNotContain("OrganizationDocument", endpointHost, StringComparison.Ordinal);

        var composition = File.ReadAllText(Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Composition",
            "Modules",
            "Organizations",
            "OrganizationModuleComposition.cs"));
        Assert.Contains("AddScoped<CreateOrganizationHandler>(provider =>", composition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<ListOrganizationsHandler>(provider =>", composition, StringComparison.Ordinal);
    }

    [Fact]
    public void TeamCreateAndList_ArePortFocusedVerticalSlicesWithCompatibilityFacades()
    {
        var moduleDirectory = Path.Combine(SourceDirectory, "Zumbo.Modules.Teams");
        var createDirectory = Path.Combine(moduleDirectory, "Application", "Features", "TeamsCore");
        var listDirectory = createDirectory;
        var representativeDirectory = Path.Combine(
            moduleDirectory,
            "Application",
            "Features",
            "RepresentativeTeamSlices");

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

        var facade = File.ReadAllText(Path.Combine(
            moduleDirectory,
            "Application",
            "Compatibility",
            "TeamService.cs"));
        Assert.Contains("createTeamHandler.HandleAsync", facade, StringComparison.Ordinal);
        Assert.Contains("listTeamsHandler.HandleAsync", facade, StringComparison.Ordinal);

        var endpointHost = File.ReadAllText(EndpointHostPath("TeamsEndpoints.cs"));
        Assert.Contains("services.AddTeamServices()", endpointHost, StringComparison.Ordinal);
        Assert.DoesNotContain("IDocumentRepository", endpointHost, StringComparison.Ordinal);
        Assert.DoesNotContain("TeamDocument", endpointHost, StringComparison.Ordinal);

        var composition = File.ReadAllText(Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Composition",
            "Modules",
            "Teams",
            "TeamModuleComposition.cs"));
        Assert.Contains("AddScoped<DurableTeamInvitationPublisher>()", composition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IDurableEventHandler, TeamInvitationNotificationHandler>()", composition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<TeamTransactionFilter>()", composition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<CreateTeamHandler>(provider =>", composition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<ListTeamsHandler>(provider =>", composition, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectCreateAndList_ArePortFocusedVerticalSlicesWithCompatibilityFacades()
    {
        var moduleDirectory = Path.Combine(SourceDirectory, "Zumbo.Modules.Projects");
        var createDirectory = Path.Combine(moduleDirectory, "Application", "Features", "ProjectsCore");
        var listDirectory = createDirectory;
        var representativeDirectory = Path.Combine(
            moduleDirectory,
            "Application",
            "Features",
            "RepresentativeProjectSlices");

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

        var facade = File.ReadAllText(Path.Combine(
            moduleDirectory,
            "Application",
            "Compatibility",
            "ProjectService.cs"));
        Assert.Contains("createProjectHandler.HandleAsync", facade, StringComparison.Ordinal);
        Assert.Contains("listProjectsHandler.HandleAsync", facade, StringComparison.Ordinal);

        var endpointHost = File.ReadAllText(EndpointHostPath("ProjectsEndpoints.cs"));
        Assert.Contains("services.AddProjectServices()", endpointHost, StringComparison.Ordinal);
        Assert.DoesNotContain("IDocumentRepository", endpointHost, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectDocument", endpointHost, StringComparison.Ordinal);

        var composition = File.ReadAllText(Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Composition",
            "Modules",
            "Projects",
            "ProjectModuleComposition.cs"));
        Assert.Contains("AddScoped<CreateProjectHandler>(provider =>", composition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<ListProjectsHandler>(provider =>", composition, StringComparison.Ordinal);
    }

    [Fact]
    public void KnowledgeDocumentRead_IsAPortFocusedVerticalSliceWithCompatibilityFacade()
    {
        var moduleDirectory = Path.Combine(SourceDirectory, "Zumbo.Modules.Projects");
        var featureDirectory = Path.Combine(moduleDirectory, "Application", "Features", "Knowledge");
        Assert.True(File.Exists(Path.Combine(featureDirectory, "GetKnowledgeDocumentQuery.cs")));
        Assert.True(File.Exists(Path.Combine(featureDirectory, "GetKnowledgeDocumentHandler.cs")));
        Assert.True(File.Exists(Path.Combine(featureDirectory, "GetKnowledgeDocumentSlice.cs")));
        foreach (var file in new[]
        {
            "GetKnowledgeVersionQuery.cs",
            "GetKnowledgeVersionHandler.cs",
            "GetKnowledgeVersionSlice.cs",
            "GetKnowledgeLinkOptionsQuery.cs",
            "GetKnowledgeLinkOptionsHandler.cs",
            "GetKnowledgeLinkOptionsSlice.cs",
            "SearchKnowledgeDocumentsQuery.cs",
            "SearchKnowledgeDocumentsHandler.cs",
            "SearchKnowledgeDocumentsSlice.cs",
            "KnowledgeReadAccess.cs",
            "KnowledgeQueryInput.cs",
            "KnowledgeResponseMapper.cs",
            "KnowledgeMutationPersistence.cs",
            "AddKnowledgeCommentCommand.cs",
            "AddKnowledgeCommentHandler.cs",
            "AddKnowledgeCommentSlice.cs",
            "ResolveKnowledgeCommentCommand.cs",
            "ResolveKnowledgeCommentHandler.cs",
            "ResolveKnowledgeCommentSlice.cs",
            "KnowledgeVersionPolicy.cs",
            "CreateKnowledgeDocumentCommand.cs",
            "CreateKnowledgeDocumentHandler.cs",
            "CreateKnowledgeDocumentSlice.cs",
            "AddKnowledgeVersionCommand.cs",
            "AddKnowledgeVersionHandler.cs",
            "AddKnowledgeVersionSlice.cs",
            "ArchiveKnowledgeDocumentCommand.cs",
            "ArchiveKnowledgeDocumentHandler.cs",
            "ArchiveKnowledgeDocumentSlice.cs"
        })
        {
            Assert.True(File.Exists(Path.Combine(featureDirectory, file)), $"Missing Knowledge slice file: {file}");
        }

        var slice = File.ReadAllText(Path.Combine(featureDirectory, "GetKnowledgeDocumentSlice.cs"));
        Assert.Contains("KnowledgeReadAccess", slice, StringComparison.Ordinal);
        Assert.Contains("KnowledgeResponseMapper.ToDocument", slice, StringComparison.Ordinal);

        var access = File.ReadAllText(Path.Combine(featureDirectory, "KnowledgeReadAccess.cs"));
        Assert.Contains("IDocumentRepository<KnowledgeDocument>", access, StringComparison.Ordinal);
        Assert.Contains("IKnowledgeDirectory", access, StringComparison.Ordinal);
        Assert.Contains("ICurrentUser", access, StringComparison.Ordinal);
        Assert.Contains("KNOWLEDGE_DOCUMENT_NOT_FOUND", access, StringComparison.Ordinal);

        var facadeDirectory = Path.Combine(
            moduleDirectory,
            "Application",
            "Compatibility",
            "Knowledge",
            "KnowledgeService");
        var facadeDelegations = new (string File, string Delegation)[]
        {
            ("KnowledgeService.Reads.cs", "getKnowledgeDocumentHandler.HandleAsync"),
            ("KnowledgeService.Reads.cs", "getKnowledgeVersionHandler.HandleAsync"),
            ("KnowledgeService.Reads.cs", "getKnowledgeLinkOptionsHandler.HandleAsync"),
            ("KnowledgeService.Reads.cs", "searchKnowledgeDocumentsHandler.HandleAsync"),
            ("KnowledgeService.Comments.cs", "addKnowledgeCommentHandler.HandleAsync"),
            ("KnowledgeService.Comments.cs", "resolveKnowledgeCommentHandler.HandleAsync"),
            ("KnowledgeService.Lifecycle.cs", "createKnowledgeDocumentHandler.HandleAsync"),
            ("KnowledgeService.Lifecycle.cs", "addKnowledgeVersionHandler.HandleAsync"),
            ("KnowledgeService.Lifecycle.cs", "archiveKnowledgeDocumentHandler.HandleAsync")
        };
        foreach (var (file, delegation) in facadeDelegations)
        {
            var facade = File.ReadAllText(Path.Combine(facadeDirectory, file));
            Assert.Contains(delegation, facade, StringComparison.Ordinal);
        }
        Assert.InRange(Directory.GetFiles(facadeDirectory, "*.cs").Length, 1, 6);

        var endpointHost = File.ReadAllText(EndpointHostPath("KnowledgeEndpoints.cs"));
        Assert.Contains("AddScoped<GetKnowledgeDocumentHandler>(provider =>", endpointHost, StringComparison.Ordinal);
        Assert.Contains("AddScoped<GetKnowledgeVersionHandler>(provider =>", endpointHost, StringComparison.Ordinal);
        Assert.Contains("AddScoped<GetKnowledgeLinkOptionsHandler>(provider =>", endpointHost, StringComparison.Ordinal);
        Assert.Contains("AddScoped<SearchKnowledgeDocumentsHandler>(provider =>", endpointHost, StringComparison.Ordinal);
        Assert.Contains("AddScoped<AddKnowledgeCommentHandler>(provider =>", endpointHost, StringComparison.Ordinal);
        Assert.Contains("AddScoped<ResolveKnowledgeCommentHandler>(provider =>", endpointHost, StringComparison.Ordinal);
        Assert.Contains("AddScoped<CreateKnowledgeDocumentHandler>(provider =>", endpointHost, StringComparison.Ordinal);
        Assert.Contains("AddScoped<AddKnowledgeVersionHandler>(provider =>", endpointHost, StringComparison.Ordinal);
        Assert.Contains("AddScoped<ArchiveKnowledgeDocumentHandler>(provider =>", endpointHost, StringComparison.Ordinal);
        Assert.Contains("[FromServices] GetKnowledgeDocumentHandler handler", endpointHost, StringComparison.Ordinal);
        Assert.Contains("[FromServices] GetKnowledgeVersionHandler handler", endpointHost, StringComparison.Ordinal);
        Assert.Contains("[FromServices] GetKnowledgeLinkOptionsHandler handler", endpointHost, StringComparison.Ordinal);
        Assert.Contains("[FromServices] SearchKnowledgeDocumentsHandler handler", endpointHost, StringComparison.Ordinal);
        Assert.Contains("[FromServices] AddKnowledgeCommentHandler handler", endpointHost, StringComparison.Ordinal);
        Assert.Contains("[FromServices] ResolveKnowledgeCommentHandler handler", endpointHost, StringComparison.Ordinal);
        Assert.Contains("[FromServices] CreateKnowledgeDocumentHandler handler", endpointHost, StringComparison.Ordinal);
        Assert.Contains("[FromServices] AddKnowledgeVersionHandler handler", endpointHost, StringComparison.Ordinal);
        Assert.Contains("[FromServices] ArchiveKnowledgeDocumentHandler handler", endpointHost, StringComparison.Ordinal);
        Assert.Contains("handler.HandleAsync", endpointHost, StringComparison.Ordinal);
        Assert.DoesNotContain("service.GetAsync(", endpointHost, StringComparison.Ordinal);
        Assert.DoesNotContain("service.GetVersionAsync(", endpointHost, StringComparison.Ordinal);
        Assert.DoesNotContain("service.GetLinkOptionsAsync(", endpointHost, StringComparison.Ordinal);
        Assert.DoesNotContain("service.SearchAsync(", endpointHost, StringComparison.Ordinal);
        Assert.DoesNotContain("service.AddCommentAsync(", endpointHost, StringComparison.Ordinal);
        Assert.DoesNotContain("service.ResolveCommentAsync(", endpointHost, StringComparison.Ordinal);
        Assert.DoesNotContain("service.CreateAsync(", endpointHost, StringComparison.Ordinal);
        Assert.DoesNotContain("service.AddVersionAsync(", endpointHost, StringComparison.Ordinal);
        Assert.DoesNotContain("service.ArchiveAsync(", endpointHost, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkItemTemplateList_IsAPortFocusedVerticalSliceWithCompatibilityFacade()
    {
        var moduleDirectory = Path.Combine(SourceDirectory, "Zumbo.Modules.WorkItems");
        var featureDirectory = Path.Combine(moduleDirectory, "Application", "Features", "Recurrences");
        Assert.True(File.Exists(Path.Combine(featureDirectory, "ListWorkItemTemplatesQuery.cs")));
        Assert.True(File.Exists(Path.Combine(featureDirectory, "ListWorkItemTemplatesHandler.cs")));
        Assert.True(File.Exists(Path.Combine(featureDirectory, "ListWorkItemTemplatesSlice.cs")));
        Assert.True(File.Exists(Path.Combine(featureDirectory, "WorkItemTemplateResponseMapper.cs")));
        foreach (var file in new[]
        {
            "RecurrenceReadAccess.cs",
            "RecurrenceResponseMapper.cs",
            "ListWorkItemRecurrencesQuery.cs",
            "ListWorkItemRecurrencesHandler.cs",
            "ListWorkItemRecurrencesSlice.cs",
            "ListRecurrenceOccurrencesQuery.cs",
            "ListRecurrenceOccurrencesHandler.cs",
            "ListRecurrenceOccurrencesSlice.cs",
            "PreviewWorkItemRecurrenceQuery.cs",
            "PreviewWorkItemRecurrenceHandler.cs",
            "PreviewWorkItemRecurrenceSlice.cs",
            "WorkItemTemplateReadAccess.cs",
            "ValidatedRecurrenceSchedule.cs",
            "RecurrenceSchedulePolicy.cs",
            "CreateWorkItemRecurrenceCommand.cs",
            "CreateWorkItemRecurrenceHandler.cs",
            "CreateWorkItemRecurrenceSlice.cs",
            "RecurrenceMutationAccess.cs",
            "SetWorkItemRecurrenceStateCommand.cs",
            "SetWorkItemRecurrenceStateHandler.cs",
            "SetWorkItemRecurrenceStateSlice.cs",
            "ArchiveWorkItemRecurrenceCommand.cs",
            "ArchiveWorkItemRecurrenceHandler.cs",
            "ArchiveWorkItemRecurrenceSlice.cs",
            "NormalizedWorkItemTemplate.cs",
            "WorkItemTemplateNormalizationPolicy.cs",
            "WorkItemTemplateMutationAccess.cs",
            "CreateWorkItemTemplateCommand.cs",
            "CreateWorkItemTemplateHandler.cs",
            "CreateWorkItemTemplateSlice.cs",
            "WorkItemTemplateUpdateAccess.cs",
            "UpdateWorkItemTemplateCommand.cs",
            "UpdateWorkItemTemplateHandler.cs",
            "UpdateWorkItemTemplateSlice.cs",
            "ArchiveWorkItemTemplateCommand.cs",
            "ArchiveWorkItemTemplateHandler.cs",
            "ArchiveWorkItemTemplateSlice.cs",
            "ScheduleDueRecurrencesCommand.cs",
            "RecurrenceOccurrenceIdentity.cs",
            "RecurrenceSchedulerAccess.cs",
            "ScheduleDueRecurrencesHandler.cs",
            "ScheduleDueRecurrencesSlice.cs"
        })
        {
            Assert.True(File.Exists(Path.Combine(featureDirectory, file)), $"Missing recurrence feature file: {file}");
        }

        var facadeDirectory = Path.Combine(
            moduleDirectory,
            "Application",
            "Compatibility",
            "Recurrences",
            "WorkItemTemplateRecurrenceService");
        var facadeFiles = Directory.GetFiles(facadeDirectory, "*.cs", SearchOption.TopDirectoryOnly);
        Assert.Equal(6, facadeFiles.Length);
        Assert.All(facadeFiles, file => Assert.Contains(
            Path.GetFileName(file),
            new[]
            {
                "TemplateFacade.cs",
                "RecurrenceFacade.cs",
                "SchedulerFacade.cs",
                "SharedSupport.cs",
                "TemplateSupport.cs",
                "RecurrenceSupport.cs"
            }));
        var facade = string.Join(
            Environment.NewLine,
            facadeFiles.Select(File.ReadAllText));
        foreach (var delegation in new[]
        {
            "listWorkItemTemplatesHandler.HandleAsync",
            "listWorkItemRecurrencesHandler.HandleAsync",
            "listRecurrenceOccurrencesHandler.HandleAsync",
            "previewWorkItemRecurrenceHandler.HandleAsync",
            "createWorkItemRecurrenceHandler.HandleAsync",
            "setWorkItemRecurrenceStateHandler.HandleAsync",
            "archiveWorkItemRecurrenceHandler.HandleAsync",
            "createWorkItemTemplateHandler.HandleAsync",
            "updateWorkItemTemplateHandler.HandleAsync",
            "archiveWorkItemTemplateHandler.HandleAsync",
            "scheduleDueRecurrencesHandler.HandleAsync"
        })
        {
            Assert.Contains(delegation, facade, StringComparison.Ordinal);
        }

        var endpoint = File.ReadAllText(Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Presentation",
            "Endpoints",
            "WorkItems",
            "Recurrences",
            "ListTemplatesEndpoint.cs"));
        Assert.Contains("ListWorkItemTemplatesHandler handler", endpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("service.ListTemplatesAsync(", endpoint, StringComparison.Ordinal);

        var recurrenceEndpointDirectory = Path.GetDirectoryName(Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Presentation",
            "Endpoints",
            "WorkItems",
            "Recurrences",
            "ListTemplatesEndpoint.cs"))!;
        var recurrenceEndpoint = File.ReadAllText(Path.Combine(
            recurrenceEndpointDirectory,
            "ListRecurrencesEndpoint.cs"));
        Assert.Contains("ListWorkItemRecurrencesHandler handler", recurrenceEndpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("service.ListRecurrencesAsync(", recurrenceEndpoint, StringComparison.Ordinal);
        var occurrenceEndpoint = File.ReadAllText(Path.Combine(
            recurrenceEndpointDirectory,
            "ListRecurrenceOccurrencesEndpoint.cs"));
        Assert.Contains("ListRecurrenceOccurrencesHandler handler", occurrenceEndpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("service.ListOccurrencesAsync(", occurrenceEndpoint, StringComparison.Ordinal);
        var previewEndpoint = File.ReadAllText(Path.Combine(
            recurrenceEndpointDirectory,
            "PreviewRecurrenceEndpoint.cs"));
        Assert.Contains("PreviewWorkItemRecurrenceHandler handler", previewEndpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("service.PreviewRecurrenceAsync(", previewEndpoint, StringComparison.Ordinal);
        var createRecurrenceEndpoint = File.ReadAllText(Path.Combine(
            recurrenceEndpointDirectory,
            "CreateRecurrenceEndpoint.cs"));
        Assert.Contains("CreateWorkItemRecurrenceHandler handler", createRecurrenceEndpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("service.CreateRecurrenceAsync(", createRecurrenceEndpoint, StringComparison.Ordinal);
        var createTemplateEndpoint = File.ReadAllText(Path.Combine(
            recurrenceEndpointDirectory,
            "CreateTemplateEndpoint.cs"));
        Assert.Contains("CreateWorkItemTemplateHandler handler", createTemplateEndpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("service.CreateTemplateAsync(", createTemplateEndpoint, StringComparison.Ordinal);
        var updateTemplateEndpoint = File.ReadAllText(Path.Combine(
            recurrenceEndpointDirectory,
            "UpdateTemplateEndpoint.cs"));
        Assert.Contains("UpdateWorkItemTemplateHandler handler", updateTemplateEndpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("service.UpdateTemplateAsync(", updateTemplateEndpoint, StringComparison.Ordinal);
        var archiveTemplateEndpoint = File.ReadAllText(Path.Combine(
            recurrenceEndpointDirectory,
            "DeleteTemplateEndpoint.cs"));
        Assert.Contains("ArchiveWorkItemTemplateHandler handler", archiveTemplateEndpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("service.ArchiveTemplateAsync(", archiveTemplateEndpoint, StringComparison.Ordinal);
        var schedulerEndpoint = File.ReadAllText(Path.Combine(
            recurrenceEndpointDirectory,
            "ProcessDueRecurrencesEndpoint.cs"));
        Assert.Contains("ScheduleDueRecurrencesHandler handler", schedulerEndpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("service.ScheduleDueAsync(", schedulerEndpoint, StringComparison.Ordinal);

        var schedulerHostedService = File.ReadAllText(Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Infrastructure",
            "BackgroundServices",
            "Recurrences",
            "WorkItemRecurrenceSchedulerHostedService.cs"));
        Assert.Contains("GetRequiredService<ScheduleDueRecurrencesHandler>()", schedulerHostedService, StringComparison.Ordinal);
        Assert.DoesNotContain("GetRequiredService<WorkItemTemplateRecurrenceService>()", schedulerHostedService, StringComparison.Ordinal);
        var stateEndpoint = File.ReadAllText(Path.Combine(
            recurrenceEndpointDirectory,
            "SetRecurrenceStateEndpoint.cs"));
        Assert.Contains("SetWorkItemRecurrenceStateHandler handler", stateEndpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("service.SetRecurrenceStateAsync(", stateEndpoint, StringComparison.Ordinal);
        var archiveEndpoint = File.ReadAllText(Path.Combine(
            recurrenceEndpointDirectory,
            "DeleteRecurrenceEndpoint.cs"));
        Assert.Contains("ArchiveWorkItemRecurrenceHandler handler", archiveEndpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("service.ArchiveRecurrenceAsync(", archiveEndpoint, StringComparison.Ordinal);

        var composition = File.ReadAllText(Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Composition",
            "Modules",
            "WorkItems",
            "WorkItemModuleComposition.cs"));
        Assert.Contains("AddScoped<ListWorkItemTemplatesHandler>(provider =>", composition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<ListWorkItemRecurrencesHandler>(provider =>", composition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<ListRecurrenceOccurrencesHandler>(provider =>", composition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<PreviewWorkItemRecurrenceHandler>(provider =>", composition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<CreateWorkItemRecurrenceHandler>(provider =>", composition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<SetWorkItemRecurrenceStateHandler>(provider =>", composition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<ArchiveWorkItemRecurrenceHandler>(provider =>", composition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<CreateWorkItemTemplateHandler>(provider =>", composition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<UpdateWorkItemTemplateHandler>(provider =>", composition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<ArchiveWorkItemTemplateHandler>(provider =>", composition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<ScheduleDueRecurrencesHandler>(provider =>", composition, StringComparison.Ordinal);
    }

    [Fact]
    public void BoardCreateAndProjectList_ArePortFocusedVerticalSlicesWithCompatibilityFacades()
    {
        var moduleDirectory = Path.Combine(SourceDirectory, "Zumbo.Modules.Boards");
        var createDirectory = Path.Combine(moduleDirectory, "Application", "Features", "BoardsCore");
        var listDirectory = createDirectory;
        var lifecycleDirectory = Path.Combine(moduleDirectory, "Application", "Features", "Lifecycle");
        var swimlaneDirectory = Path.Combine(moduleDirectory, "Application", "Features", "Swimlanes");
        var columnsDirectory = Path.Combine(moduleDirectory, "Application", "Features", "Columns");
        var representativeDirectory = Path.Combine(
            moduleDirectory,
            "Application",
            "Features",
            "RepresentativeBoardSlices");

        Assert.True(File.Exists(Path.Combine(createDirectory, "CreateBoardRequest.cs")));
        Assert.True(File.Exists(Path.Combine(createDirectory, "CreateBoardValidator.cs")));
        Assert.True(File.Exists(Path.Combine(createDirectory, "CreateBoardHandler.cs")));
        Assert.True(File.Exists(Path.Combine(createDirectory, "CreateBoardSlice.cs")));
        Assert.True(File.Exists(Path.Combine(createDirectory, "UpdateBoardValidator.cs")));
        Assert.True(File.Exists(Path.Combine(createDirectory, "UpdateBoardHandler.cs")));
        Assert.True(File.Exists(Path.Combine(lifecycleDirectory, "ArchiveBoardCommand.cs")));
        Assert.True(File.Exists(Path.Combine(lifecycleDirectory, "ArchiveBoardValidator.cs")));
        Assert.True(File.Exists(Path.Combine(lifecycleDirectory, "ArchiveBoardHandler.cs")));
        Assert.True(File.Exists(Path.Combine(lifecycleDirectory, "RestoreBoardCommand.cs")));
        Assert.True(File.Exists(Path.Combine(lifecycleDirectory, "RestoreBoardValidator.cs")));
        Assert.True(File.Exists(Path.Combine(lifecycleDirectory, "RestoreBoardHandler.cs")));
        Assert.True(File.Exists(Path.Combine(swimlaneDirectory, "UpdateSwimlaneValidator.cs")));
        Assert.True(File.Exists(Path.Combine(swimlaneDirectory, "UpdateSwimlaneHandler.cs")));
        Assert.True(File.Exists(Path.Combine(columnsDirectory, "AddColumnValidator.cs")));
        Assert.True(File.Exists(Path.Combine(columnsDirectory, "AddColumnHandler.cs")));
        Assert.True(File.Exists(Path.Combine(columnsDirectory, "UpdateColumnValidator.cs")));
        Assert.True(File.Exists(Path.Combine(columnsDirectory, "UpdateColumnHandler.cs")));
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
        var updateHandler = File.ReadAllText(Path.Combine(createDirectory, "UpdateBoardHandler.cs"));
        Assert.DoesNotContain("BoardService", createSlice, StringComparison.Ordinal);
        Assert.DoesNotContain("BoardService", listSlice, StringComparison.Ordinal);
        Assert.DoesNotContain("BoardService", updateHandler, StringComparison.Ordinal);
        Assert.Contains("IDocumentRepository<BoardDocument>", createSlice, StringComparison.Ordinal);
        Assert.Contains("IBoardProjectAccessChecker", createSlice, StringComparison.Ordinal);
        Assert.Contains("IDistributedLockProvider", createSlice, StringComparison.Ordinal);
        Assert.Contains("IBoardAuditWriter", createSlice, StringComparison.Ordinal);
        Assert.Contains("IDocumentRepository<BoardDocument>", listSlice, StringComparison.Ordinal);
        Assert.Contains("ICurrentUser", listSlice, StringComparison.Ordinal);
        Assert.Contains("IDocumentRepository<BoardDocument>", updateHandler, StringComparison.Ordinal);
        Assert.Contains("IBoardProjectAccessChecker", updateHandler, StringComparison.Ordinal);
        Assert.Contains("IBoardAuditWriter", updateHandler, StringComparison.Ordinal);

        var boardsFacade = File.ReadAllText(Path.Combine(
            moduleDirectory,
            "Application",
            "Compatibility",
            "BoardService",
            "BoardService.Boards.cs"));
        Assert.Contains("createBoardHandler.HandleAsync", boardsFacade, StringComparison.Ordinal);
        Assert.Contains("listBoardsByProjectHandler.HandleAsync", boardsFacade, StringComparison.Ordinal);
        Assert.Contains("updateBoardHandler.HandleAsync", boardsFacade, StringComparison.Ordinal);
        var lifecycleFacade = File.ReadAllText(Path.Combine(
            moduleDirectory,
            "Application",
            "Compatibility",
            "BoardService",
            "BoardService.Lifecycle.cs"));
        Assert.Contains("archiveBoardHandler.HandleAsync", lifecycleFacade, StringComparison.Ordinal);
        Assert.Contains("restoreBoardHandler.HandleAsync", lifecycleFacade, StringComparison.Ordinal);
        var swimlaneFacade = File.ReadAllText(Path.Combine(
            moduleDirectory,
            "Application",
            "Compatibility",
            "BoardService",
            "BoardService.Swimlanes.cs"));
        Assert.Contains("updateSwimlaneHandler.HandleAsync", swimlaneFacade, StringComparison.Ordinal);
        var columnsFacade = File.ReadAllText(Path.Combine(
            moduleDirectory,
            "Application",
            "Compatibility",
            "BoardService",
            "BoardService.Columns.cs"));
        Assert.Contains("addColumnHandler.HandleAsync", columnsFacade, StringComparison.Ordinal);
        Assert.Contains("updateColumnHandler.HandleAsync", columnsFacade, StringComparison.Ordinal);

        var endpointHost = File.ReadAllText(EndpointHostPath("BoardsEndpoints.cs"));
        Assert.Contains("services.AddBoardServices()", endpointHost, StringComparison.Ordinal);
        Assert.DoesNotContain("IDocumentRepository", endpointHost, StringComparison.Ordinal);
        Assert.DoesNotContain("BoardDocument", endpointHost, StringComparison.Ordinal);

        var composition = File.ReadAllText(Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Composition",
            "Modules",
            "Boards",
            "BoardModuleComposition.cs"));
        Assert.Contains("AddScoped<CreateBoardHandler>(provider =>", composition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<ListBoardsByProjectHandler>(provider =>", composition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<UpdateBoardHandler>()", composition, StringComparison.Ordinal);
        Assert.Contains("UpdateBoardHandler handler", endpointHost, StringComparison.Ordinal);
        Assert.Contains("AddScoped<ArchiveBoardHandler>()", composition, StringComparison.Ordinal);
        Assert.Contains("ArchiveBoardHandler handler", endpointHost, StringComparison.Ordinal);
        Assert.Contains("AddScoped<RestoreBoardHandler>()", composition, StringComparison.Ordinal);
        Assert.Contains("RestoreBoardHandler handler", endpointHost, StringComparison.Ordinal);
        Assert.Contains("AddScoped<UpdateSwimlaneHandler>()", composition, StringComparison.Ordinal);
        Assert.Contains("UpdateSwimlaneHandler handler", endpointHost, StringComparison.Ordinal);
        Assert.Contains("AddScoped<AddColumnHandler>()", composition, StringComparison.Ordinal);
        Assert.Contains("AddColumnHandler handler", endpointHost, StringComparison.Ordinal);
        Assert.Contains("AddScoped<UpdateColumnHandler>()", composition, StringComparison.Ordinal);
        Assert.Contains("UpdateColumnHandler handler", endpointHost, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkflowUpsertAndRead_ArePortFocusedVerticalSlicesWithCompatibilityFacades()
    {
        var moduleDirectory = Path.Combine(SourceDirectory, "Zumbo.Modules.Workflows");
        var upsertDirectory = Path.Combine(moduleDirectory, "Application", "Features", "WorkflowDefinitions");
        var getDirectory = upsertDirectory;
        var representativeDirectory = Path.Combine(
            moduleDirectory,
            "Application",
            "Features",
            "RepresentativeWorkflowSlices");

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
            "Application",
            "Compatibility",
            "WorkflowDefinitions",
            "WorkflowService.cs"));
        Assert.Contains("upsertWorkflowHandler.HandleAsync", facade, StringComparison.Ordinal);
        Assert.Contains("getWorkflowHandler.HandleAsync", facade, StringComparison.Ordinal);

        var endpointHost = File.ReadAllText(EndpointHostPath("WorkflowEndpoints.cs"));
        Assert.Contains("services.AddWorkflowServices(configuration)", endpointHost, StringComparison.Ordinal);
        Assert.DoesNotContain("IDocumentRepository", endpointHost, StringComparison.Ordinal);
        Assert.DoesNotContain("WorkflowDefinitionDocument", endpointHost, StringComparison.Ordinal);

        var composition = File.ReadAllText(Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Composition",
            "Modules",
            "Workflows",
            "WorkflowModuleComposition.cs"));
        Assert.Contains("AddScoped<UpsertWorkflowHandler>(provider =>", composition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<GetWorkflowHandler>(provider =>", composition, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkItemCreateAndSearch_ArePortFocusedVerticalSlicesWithCompatibilityFacades()
    {
        var moduleDirectory = Path.Combine(SourceDirectory, "Zumbo.Modules.WorkItems");
        var createDirectory = Path.Combine(moduleDirectory, "Application", "Features", "WorkItemsCore");
        var searchDirectory = Path.Combine(moduleDirectory, "Application", "Features", "Search");
        var representativeDirectory = Path.Combine(
            moduleDirectory,
            "Application",
            "Features",
            "RepresentativeWorkItemSlices");

        Assert.True(File.Exists(Path.Combine(createDirectory, "CreateWorkItemRequest.cs")));
        Assert.True(File.Exists(Path.Combine(createDirectory, "CreateWorkItemContext.cs")));
        Assert.True(File.Exists(Path.Combine(createDirectory, "CreateWorkItemValidator.cs")));
        Assert.True(File.Exists(Path.Combine(createDirectory, "CreateWorkItemHandler.cs")));
        Assert.True(File.Exists(Path.Combine(createDirectory, "CreateWorkItemSlice.cs")));
        var intakeCreatorPath = Path.Combine(
            moduleDirectory,
            "Application",
            "Features",
            "Intake",
            "CreateIntakeWorkItemHandler.cs");
        Assert.True(File.Exists(intakeCreatorPath));
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
        Assert.Contains("IntakeStableIds.WorkItemId", createSlice, StringComparison.Ordinal);
        Assert.Contains("SourceIntakeSubmissionId = context.IntakeSubmissionId", createSlice, StringComparison.Ordinal);
        Assert.Contains("IDocumentRepository<WorkItemDocument>", searchSlice, StringComparison.Ordinal);
        Assert.Contains("IProjectPermissionChecker", searchSlice, StringComparison.Ordinal);
        Assert.Contains("IWorkItemSearchIndex", searchSlice, StringComparison.Ordinal);

        var createFacade = File.ReadAllText(Path.Combine(
            moduleDirectory,
            "Application",
            "Compatibility",
            "WorkItemService",
            "WorkItemService.Create.cs"));
        var searchFacade = File.ReadAllText(Path.Combine(
            moduleDirectory,
            "Application",
            "Compatibility",
            "WorkItemService",
            "WorkItemService.Read.cs"));
        Assert.Contains("createWorkItemHandler.HandleAsync", createFacade, StringComparison.Ordinal);
        Assert.Contains("createWorkItemHandler.CreateAsync", createFacade, StringComparison.Ordinal);
        Assert.Contains("createWorkItemHandler.HandleScopedAsync", createFacade, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateWorkItemValidator.Validate", createFacade, StringComparison.Ordinal);
        Assert.DoesNotContain("workItems.CreateAsync", createFacade, StringComparison.Ordinal);
        Assert.DoesNotContain("activityStore.CreateTimelineAsync", createFacade, StringComparison.Ordinal);
        Assert.Contains("searchWorkItemsHandler.HandleAsync", searchFacade, StringComparison.Ordinal);

        var intakeCreator = File.ReadAllText(intakeCreatorPath);
        Assert.Contains(": IIntakeWorkItemCreator", intakeCreator, StringComparison.Ordinal);
        Assert.Contains("createWorkItemHandler.CreateAsync", intakeCreator, StringComparison.Ordinal);

        var composition = File.ReadAllText(Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Composition",
            "Modules",
            "WorkItems",
            "WorkItemModuleComposition.cs"));
        Assert.Contains("services.AddWorkItemCoreCreateAndReadHandlers();", composition, StringComparison.Ordinal);
        Assert.Contains(
            "services.AddWorkItemIntakeServices();",
            composition,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "provider.GetRequiredService<WorkItemService>()",
            composition,
            StringComparison.Ordinal);
        Assert.Contains("services.AddWorkItemCoreUpdateHandler();", composition, StringComparison.Ordinal);

        var coreComposition = File.ReadAllText(Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Composition",
            "Modules",
            "WorkItems",
            "WorkItemCoreComposition.cs"));
        Assert.Contains("AddScoped<CreateWorkItemHandler>(provider =>", coreComposition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<SearchWorkItemsHandler>(provider =>", coreComposition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<GetWorkItemHandler>(provider =>", coreComposition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<ArchiveWorkItemHandler>(provider =>", coreComposition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<RestoreWorkItemHandler>(provider =>", coreComposition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<AddLabelHandler>(provider =>", coreComposition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<RemoveLabelHandler>(provider =>", coreComposition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<UpdateWorkItemHandler>(provider =>", coreComposition, StringComparison.Ordinal);
        Assert.Contains("services.AddWorkItemPlanningHandlers();", composition, StringComparison.Ordinal);

        var planningComposition = File.ReadAllText(Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Composition",
            "Modules",
            "WorkItems",
            "WorkItemPlanningComposition.cs"));
        Assert.Contains("AddScoped<SetPlanningHandler>(provider =>", planningComposition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<MoveWorkItemHandler>(provider =>", planningComposition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<ReorderWorkItemHandler>(provider =>", planningComposition, StringComparison.Ordinal);

        Assert.Contains("services.AddWorkItemChecklistHandlers();", composition, StringComparison.Ordinal);
        var checklistComposition = File.ReadAllText(Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Composition",
            "Modules",
            "WorkItems",
            "WorkItemChecklistComposition.cs"));
        Assert.Contains("AddScoped<AddChecklistItemHandler>(provider =>", checklistComposition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<CompleteChecklistItemHandler>(provider =>", checklistComposition, StringComparison.Ordinal);

        Assert.Contains("services.AddWorkItemWorklogHandlers();", composition, StringComparison.Ordinal);
        var worklogComposition = File.ReadAllText(Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Composition",
            "Modules",
            "WorkItems",
            "WorkItemWorklogComposition.cs"));
        Assert.Contains("AddScoped<AddWorkLogHandler>(provider =>", worklogComposition, StringComparison.Ordinal);

        Assert.Contains("services.AddWorkItemAssignmentHandlers();", composition, StringComparison.Ordinal);
        var assignmentComposition = File.ReadAllText(Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Composition",
            "Modules",
            "WorkItems",
            "WorkItemAssignmentComposition.cs"));
        Assert.Contains("AddScoped<ClearAssigneeHandler>(provider =>", assignmentComposition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<AssignWorkItemHandler>(provider =>", assignmentComposition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<SetWorkItemTeamHandler>(provider =>", assignmentComposition, StringComparison.Ordinal);

        Assert.Contains("services.AddWorkItemCustomFieldsHandlers();", composition, StringComparison.Ordinal);
        var customFieldsComposition = File.ReadAllText(Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Composition",
            "Modules",
            "WorkItems",
            "WorkItemCustomFieldsComposition.cs"));
        Assert.Contains("AddScoped<SetCustomFieldsHandler>(provider =>", customFieldsComposition, StringComparison.Ordinal);

        Assert.Contains("services.AddWorkItemApprovalRequestHandler();", composition, StringComparison.Ordinal);
        Assert.Contains("services.AddWorkItemApprovalDecisionHandler();", composition, StringComparison.Ordinal);
        Assert.DoesNotContain("IDocumentRepository", composition, StringComparison.Ordinal);
        Assert.DoesNotContain("WorkItemDocument", composition, StringComparison.Ordinal);
        var approvalComposition = File.ReadAllText(Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Composition",
            "Modules",
            "WorkItems",
            "WorkItemApprovalComposition.cs"));
        Assert.Contains("AddScoped<RequestApprovalHandler>(provider =>", approvalComposition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<DecideApprovalHandler>(provider =>", approvalComposition, StringComparison.Ordinal);

        var intakeComposition = File.ReadAllText(Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Composition",
            "Modules",
            "WorkItems",
            "WorkItemIntakeComposition.cs"));
        Assert.Contains(
            "new CreateIntakeWorkItemHandler(provider.GetRequiredService<CreateWorkItemHandler>())",
            intakeComposition,
            StringComparison.Ordinal);
        Assert.Contains("AddOptions<IntakeOptions>()", intakeComposition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IntakeFormService>()", intakeComposition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IntakeSubmissionService>()", intakeComposition, StringComparison.Ordinal);
        Assert.Contains("services.AddWorkItemWebhookServices();", composition, StringComparison.Ordinal);

        var webhookComposition = File.ReadAllText(Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Composition",
            "Modules",
            "WorkItems",
            "WorkItemWebhookComposition.cs"));
        Assert.Contains("AddOptions<WebhookOptions>()", webhookComposition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<CreateSubscriptionHandler>()", webhookComposition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<DispatchDeliveriesHandler>()", webhookComposition, StringComparison.Ordinal);
        Assert.Contains("provider.GetRequiredService<QueueDeliveryHandler>()", webhookComposition, StringComparison.Ordinal);
        Assert.Contains("services.AddDevelopmentIntegrationServices();", composition, StringComparison.Ordinal);

        var developmentComposition = File.ReadAllText(Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Composition",
            "Modules",
            "WorkItems",
            "DevelopmentIntegrationComposition.cs"));
        Assert.Contains("AddOptions<DevelopmentProviderOptions>()", developmentComposition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<CreateConnectionHandler>()", developmentComposition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<ProcessWebhookHandler>()", developmentComposition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<ListRepositoriesHandler>()", developmentComposition, StringComparison.Ordinal);
        Assert.Contains("services.AddWorkItemPublicationServices();", composition, StringComparison.Ordinal);
        Assert.Contains("services.AddWorkItemDurableEventHandlers();", composition, StringComparison.Ordinal);

        var messagingComposition = File.ReadAllText(Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Composition",
            "Modules",
            "WorkItems",
            "WorkItemMessagingComposition.cs"));
        Assert.Contains("AddScoped<DurableWorkItemEventPublisher>()", messagingComposition, StringComparison.Ordinal);
        Assert.Contains("IWorkItemAuditPublisher", messagingComposition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IDurableEventHandler, WorkItemAuditDurableHandler>()", messagingComposition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IDurableEventHandler, DevelopmentWebhookProcessingDurableHandler>()", messagingComposition, StringComparison.Ordinal);

        var searchEndpoint = File.ReadAllText(Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Presentation",
            "Endpoints",
            "WorkItems",
            "Search",
            "SearchWorkItemsPageEndpoint.cs"));
        Assert.Contains("SearchWorkItemsHandler handler", searchEndpoint, StringComparison.Ordinal);
        Assert.Contains("handler.HandlePageAsync(request, ct)", searchEndpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("WorkItemService service", searchEndpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("service.SearchPageAsync", searchEndpoint, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkItemSchemaUseCases_ArePortFocusedVerticalSlicesWithCompatibilityFacade()
    {
        var moduleDirectory = Path.Combine(SourceDirectory, "Zumbo.Modules.WorkItems");
        var featureDirectory = Path.Combine(moduleDirectory, "Application", "Features", "Schema");
        foreach (var file in new[]
        {
            "GetWorkItemTypeSchemaQuery.cs",
            "GetWorkItemTypeSchemaHandler.cs",
            "GetWorkItemTypeSchemaSlice.cs",
            "GetIssueTypeDistributionQuery.cs",
            "GetIssueTypeDistributionHandler.cs",
            "GetIssueTypeDistributionSlice.cs",
            "GetCustomFieldDistributionQuery.cs",
            "GetCustomFieldDistributionHandler.cs",
            "GetCustomFieldDistributionSlice.cs",
            "WorkItemTypeSchemaReadAccess.cs",
            "WorkItemTypeSchemaResponseMapper.cs",
            "UpsertWorkItemTypeSchemaCommand.cs",
            "UpsertWorkItemTypeSchemaHandler.cs",
            "UpsertWorkItemTypeSchemaSlice.cs",
            "WorkItemTypeSchemaDefinitionPolicy.cs",
            "ValidateWorkItemShapeQuery.cs",
            "ValidateWorkItemShapeHandler.cs",
            "ValidateWorkItemShapeSlice.cs",
            "GetIssueTypeHierarchyQuery.cs",
            "GetIssueTypeHierarchyHandler.cs",
            "GetIssueTypeHierarchySlice.cs",
            "ValidateWorkItemSearchFilterQuery.cs",
            "ValidateWorkItemSearchFilterHandler.cs",
            "ValidateWorkItemSearchFilterSlice.cs",
            "WorkItemTypeSchemaPolicyAccess.cs",
            "WorkItemTypeSchemaPolicyAdapter.cs"
        })
        {
            Assert.True(File.Exists(Path.Combine(featureDirectory, file)), $"Missing schema read file: {file}");
        }

        var facadeDirectory = Path.Combine(
            moduleDirectory,
            "Application",
            "Compatibility",
            "Schema",
            "WorkItemTypeSchemaService");
        var facadeFiles = Directory.GetFiles(facadeDirectory, "*.cs", SearchOption.TopDirectoryOnly);
        Assert.Equal(5, facadeFiles.Length);
        AssertExactSet(
            new[]
            {
                "Facade.cs",
                "PersistenceSupport.cs",
                "DefinitionSupport.cs",
                "ValidationSupport.cs",
                "MappingSupport.cs"
            },
            facadeFiles.Select(Path.GetFileName)!);
        var facade = string.Join(Environment.NewLine, facadeFiles.Select(File.ReadAllText));
        foreach (var delegation in new[]
        {
            "getWorkItemTypeSchemaHandler.HandleAsync",
            "getIssueTypeDistributionHandler.HandleAsync",
            "getCustomFieldDistributionHandler.HandleAsync",
            "upsertWorkItemTypeSchemaHandler.HandleAsync",
            "validateWorkItemShapeHandler.HandleAsync",
            "getIssueTypeHierarchyHandler.HandleAsync",
            "validateWorkItemSearchFilterHandler.HandleAsync"
        })
        {
            Assert.Contains(delegation, facade, StringComparison.Ordinal);
        }

        var endpoint = File.ReadAllText(Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Presentation",
            "Endpoints",
            "WorkItems",
            "Schema",
            "WorkItemTypeSchemaEndpoints.cs"));
        Assert.Contains("GetWorkItemTypeSchemaHandler handler", endpoint, StringComparison.Ordinal);
        Assert.Contains("GetIssueTypeDistributionHandler handler", endpoint, StringComparison.Ordinal);
        Assert.Contains("GetCustomFieldDistributionHandler handler", endpoint, StringComparison.Ordinal);
        Assert.Contains("UpsertWorkItemTypeSchemaHandler handler", endpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("service.GetAsync(projectId", endpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("service.GetIssueTypeDistributionAsync", endpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("service.GetCustomFieldDistributionAsync", endpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("service.UpsertAsync", endpoint, StringComparison.Ordinal);

        var composition = File.ReadAllText(Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Composition",
            "Modules",
            "WorkItems",
            "WorkItemModuleComposition.cs"));
        Assert.Contains("AddScoped<GetWorkItemTypeSchemaHandler>(provider =>", composition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<GetIssueTypeDistributionHandler>(provider =>", composition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<GetCustomFieldDistributionHandler>(provider =>", composition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<UpsertWorkItemTypeSchemaHandler>(provider =>", composition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<ValidateWorkItemShapeHandler>(provider =>", composition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<GetIssueTypeHierarchyHandler>(provider =>", composition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<ValidateWorkItemSearchFilterHandler>(provider =>", composition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<IWorkItemTypeSchemaPolicy, WorkItemTypeSchemaPolicyAdapter>()", composition, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "provider.GetRequiredService<WorkItemTypeSchemaService>()",
            composition,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PortfolioUseCases_ArePortFocusedVerticalSlicesWithCompatibilityFacade()
    {
        var moduleDirectory = Path.Combine(SourceDirectory, "Zumbo.Modules.Projects");
        var featureDirectory = Path.Combine(moduleDirectory, "Application", "Features", "Portfolio");
        foreach (var file in new[]
        {
            "ListPortfoliosQuery.cs",
            "ListPortfoliosHandler.cs",
            "ListPortfoliosSlice.cs",
            "GetPortfolioQuery.cs",
            "GetPortfolioHandler.cs",
            "GetPortfolioSlice.cs",
            "GetPortfolioRoadmapQuery.cs",
            "GetPortfolioRoadmapHandler.cs",
            "GetPortfolioRoadmapSlice.cs",
            "PortfolioReadAccess.cs",
            "PortfolioResponseMapper.cs",
            "PortfolioMutationPersistence.cs",
            "PortfolioValidation.cs",
            "PortfolioMutationMapper.cs",
            "SavePortfolioCommand.cs",
            "SavePortfolioHandler.cs",
            "SavePortfolioSlice.cs",
            "ArchivePortfolioCommand.cs",
            "ArchivePortfolioHandler.cs",
            "ArchivePortfolioSlice.cs",
            "SaveInitiativeCommand.cs",
            "SaveInitiativeHandler.cs",
            "SaveInitiativeSlice.cs",
            "AddInitiativeStatusUpdateCommand.cs",
            "AddInitiativeStatusUpdateHandler.cs",
            "AddInitiativeStatusUpdateSlice.cs",
            "SavePortfolioDependencyCommand.cs",
            "SavePortfolioDependencyHandler.cs",
            "SavePortfolioDependencySlice.cs"
        })
        {
            Assert.True(File.Exists(Path.Combine(featureDirectory, file)), $"Missing portfolio read file: {file}");
        }

        var facadeDirectory = Path.Combine(
            moduleDirectory,
            "Application",
            "Compatibility",
            "Portfolio",
            "PortfolioService");
        Assert.Equal(7, Directory.GetFiles(facadeDirectory, "*.cs").Length);
        foreach (var (file, delegation) in new[]
        {
            ("ReadsFacade.cs", "new ListPortfoliosHandler(portfolios, currentUser).HandleAsync"),
            ("ReadsFacade.cs", "new GetPortfolioHandler(portfolios, currentUser).HandleAsync"),
            ("ReadsFacade.cs", "new GetPortfolioRoadmapHandler("),
            ("LifecycleFacade.cs", "new SavePortfolioHandler("),
            ("LifecycleFacade.cs", "new ArchivePortfolioHandler("),
            ("InitiativesFacade.cs", "new SaveInitiativeHandler("),
            ("InitiativesFacade.cs", "new AddInitiativeStatusUpdateHandler("),
            ("DependenciesFacade.cs", "new SavePortfolioDependencyHandler(")
        })
        {
            Assert.Contains(
                delegation,
                File.ReadAllText(Path.Combine(facadeDirectory, file)),
                StringComparison.Ordinal);
        }

        var endpoint = File.ReadAllText(Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Presentation",
            "Endpoints",
            "Projects",
            "Portfolio",
            "PortfolioEndpoints.cs"));
        Assert.Contains("ListPortfoliosHandler handler", endpoint, StringComparison.Ordinal);
        Assert.Contains("GetPortfolioHandler handler", endpoint, StringComparison.Ordinal);
        Assert.Contains("GetPortfolioRoadmapHandler handler", endpoint, StringComparison.Ordinal);
        Assert.Contains("SavePortfolioHandler handler", endpoint, StringComparison.Ordinal);
        Assert.Contains("ArchivePortfolioHandler handler", endpoint, StringComparison.Ordinal);
        Assert.Contains("SaveInitiativeHandler handler", endpoint, StringComparison.Ordinal);
        Assert.Contains("AddInitiativeStatusUpdateHandler handler", endpoint, StringComparison.Ordinal);
        Assert.Contains("SavePortfolioDependencyHandler handler", endpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("service.ListAsync(", endpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("service.GetAsync(", endpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("service.GetRoadmapAsync(", endpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("service.SaveAsync(", endpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("service.ArchiveAsync(", endpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("service.SaveInitiativeAsync(", endpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("service.AddStatusUpdateAsync(", endpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("service.SaveDependencyAsync(", endpoint, StringComparison.Ordinal);
        Assert.Contains("AddScoped<ListPortfoliosHandler>(provider =>", endpoint, StringComparison.Ordinal);
        Assert.Contains("AddScoped<GetPortfolioHandler>(provider =>", endpoint, StringComparison.Ordinal);
        Assert.Contains("AddScoped<GetPortfolioRoadmapHandler>(provider =>", endpoint, StringComparison.Ordinal);
        Assert.Contains("AddScoped<SavePortfolioHandler>(provider =>", endpoint, StringComparison.Ordinal);
        Assert.Contains("AddScoped<ArchivePortfolioHandler>(provider =>", endpoint, StringComparison.Ordinal);
        Assert.Contains("AddScoped<SaveInitiativeHandler>(provider =>", endpoint, StringComparison.Ordinal);
        Assert.Contains("AddScoped<AddInitiativeStatusUpdateHandler>(provider =>", endpoint, StringComparison.Ordinal);
        Assert.Contains("AddScoped<SavePortfolioDependencyHandler>(provider =>", endpoint, StringComparison.Ordinal);
    }

    [Fact]
    public void GoalUseCases_ArePortFocusedVerticalSlicesWithCompatibilityFacade()
    {
        var moduleDirectory = Path.Combine(SourceDirectory, "Zumbo.Modules.Projects");
        var featureDirectory = Path.Combine(moduleDirectory, "Application", "Features", "Goals");
        foreach (var file in new[]
        {
            "GetGoalQuery.cs",
            "GetGoalHandler.cs",
            "GetGoalSlice.cs",
            "ListGoalsQuery.cs",
            "ListGoalsHandler.cs",
            "ListGoalsSlice.cs",
            "GetGoalRollupQuery.cs",
            "GetGoalRollupHandler.cs",
            "GetGoalRollupSlice.cs",
            "GoalReadAccess.cs",
            "GoalResponseMapper.cs",
            "GoalMutationPersistence.cs",
            "GoalValidation.cs",
            "AddKeyResultProgressCommand.cs",
            "AddKeyResultProgressHandler.cs",
            "AddKeyResultProgressSlice.cs",
            "AddGoalStatusUpdateCommand.cs",
            "AddGoalStatusUpdateHandler.cs",
            "AddGoalStatusUpdateSlice.cs",
            "GoalRequestNormalizer.cs",
            "NormalizedGoalRequest.cs",
            "GoalMutationMapper.cs",
            "SaveGoalCommand.cs",
            "SaveGoalHandler.cs",
            "SaveGoalSlice.cs",
            "SaveKeyResultCommand.cs",
            "SaveKeyResultHandler.cs",
            "SaveKeyResultSlice.cs",
            "ArchiveGoalCommand.cs",
            "ArchiveGoalHandler.cs",
            "ArchiveGoalSlice.cs"
        })
        {
            Assert.True(File.Exists(Path.Combine(featureDirectory, file)), $"Missing goal read file: {file}");
        }

        var facadeDirectory = Path.Combine(
            moduleDirectory,
            "Application",
            "Compatibility",
            "Goals",
            "GoalService");
        Assert.Equal(7, Directory.GetFiles(facadeDirectory, "*.cs").Length);
        foreach (var (file, delegation) in new[]
        {
            ("ReadsFacade.cs", "new GetGoalHandler(goals, currentUser).HandleAsync"),
            ("ReadsFacade.cs", "new ListGoalsHandler(goals, currentUser).HandleAsync"),
            ("ReadsFacade.cs", "new GetGoalRollupHandler(goals, directory, currentUser, clock).HandleAsync"),
            ("UpdatesFacade.cs", "new AddKeyResultProgressHandler("),
            ("UpdatesFacade.cs", "new AddGoalStatusUpdateHandler("),
            ("LifecycleFacade.cs", "new SaveGoalHandler("),
            ("LifecycleFacade.cs", "new SaveKeyResultHandler("),
            ("LifecycleFacade.cs", "new ArchiveGoalHandler(")
        })
        {
            Assert.Contains(
                delegation,
                File.ReadAllText(Path.Combine(facadeDirectory, file)),
                StringComparison.Ordinal);
        }

        var endpoint = File.ReadAllText(Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Presentation",
            "Endpoints",
            "Projects",
            "Goals",
            "GoalEndpoints.cs"));
        Assert.Contains("ListGoalsHandler handler", endpoint, StringComparison.Ordinal);
        Assert.Contains("GetGoalHandler handler", endpoint, StringComparison.Ordinal);
        Assert.Contains("GetGoalRollupHandler handler", endpoint, StringComparison.Ordinal);
        Assert.Contains("AddKeyResultProgressHandler handler", endpoint, StringComparison.Ordinal);
        Assert.Contains("AddGoalStatusUpdateHandler handler", endpoint, StringComparison.Ordinal);
        Assert.Contains("SaveGoalHandler handler", endpoint, StringComparison.Ordinal);
        Assert.Contains("SaveKeyResultHandler handler", endpoint, StringComparison.Ordinal);
        Assert.Contains("ArchiveGoalHandler handler", endpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("service.ListAsync(", endpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("service.GetAsync(", endpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("service.GetRollupAsync(", endpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("service.AddKeyResultProgressAsync(", endpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("service.AddStatusUpdateAsync(", endpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("service.SaveAsync(", endpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("service.SaveKeyResultAsync(", endpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("service.ArchiveAsync(", endpoint, StringComparison.Ordinal);
        Assert.Contains("AddScoped<ListGoalsHandler>(provider =>", endpoint, StringComparison.Ordinal);
        Assert.Contains("AddScoped<GetGoalHandler>(provider =>", endpoint, StringComparison.Ordinal);
        Assert.Contains("AddScoped<GetGoalRollupHandler>(provider =>", endpoint, StringComparison.Ordinal);
        Assert.Contains("AddScoped<AddKeyResultProgressHandler>(provider =>", endpoint, StringComparison.Ordinal);
        Assert.Contains("AddScoped<AddGoalStatusUpdateHandler>(provider =>", endpoint, StringComparison.Ordinal);
        Assert.Contains("AddScoped<SaveGoalHandler>(provider =>", endpoint, StringComparison.Ordinal);
        Assert.Contains("AddScoped<SaveKeyResultHandler>(provider =>", endpoint, StringComparison.Ordinal);
        Assert.Contains("AddScoped<ArchiveGoalHandler>(provider =>", endpoint, StringComparison.Ordinal);
    }

    [Fact]
    public void SprintUseCases_ArePortFocusedVerticalSlicesWithCompatibilityFacade()
    {
        var moduleDirectory = Path.Combine(SourceDirectory, "Zumbo.Modules.WorkItems");
        var featureDirectory = Path.Combine(moduleDirectory, "Application", "Features", "Sprints");
        foreach (var file in new[]
        {
            "GetSprintQuery.cs",
            "GetSprintHandler.cs",
            "GetSprintSlice.cs",
            "ListSprintsQuery.cs",
            "ListSprintsHandler.cs",
            "ListSprintsSlice.cs",
            "ListSprintBacklogQuery.cs",
            "ListSprintBacklogHandler.cs",
            "ListSprintBacklogSlice.cs",
            "SprintReadAccess.cs",
            "SprintResponseMapper.cs",
            "GetSprintBurndownQuery.cs",
            "GetSprintBurndownHandler.cs",
            "GetSprintBurndownSlice.cs",
            "GetSprintVelocityQuery.cs",
            "GetSprintVelocityHandler.cs",
            "GetSprintVelocitySlice.cs",
            "CreateSprintCommand.cs",
            "CreateSprintHandler.cs",
            "CreateSprintSlice.cs",
            "StartSprintCommand.cs",
            "StartSprintHandler.cs",
            "StartSprintSlice.cs",
            "CompleteSprintCommand.cs",
            "CompleteSprintHandler.cs",
            "CompleteSprintSlice.cs",
            "PlanSprintWorkItemCommand.cs",
            "PlanSprintWorkItemHandler.cs",
            "PlanSprintWorkItemSlice.cs",
            "UnplanSprintWorkItemCommand.cs",
            "UnplanSprintWorkItemHandler.cs",
            "UnplanSprintWorkItemSlice.cs"
        })
        {
            Assert.True(File.Exists(Path.Combine(featureDirectory, file)), $"Missing sprint read file: {file}");
        }

        var facadeDirectory = Path.Combine(
            moduleDirectory,
            "Application",
            "Compatibility",
            "Sprints",
            "SprintService");
        Assert.Equal(7, Directory.GetFiles(facadeDirectory, "*.cs").Length);
        foreach (var (file, delegation) in new[]
        {
            ("ReadsFacade.cs", "getSprintHandler.HandleAsync"),
            ("ReadsFacade.cs", "listSprintsHandler.HandleAsync"),
            ("ReadsFacade.cs", "listSprintBacklogHandler.HandleAsync"),
            ("ReportsFacade.cs", "getSprintBurndownHandler.HandleAsync"),
            ("ReportsFacade.cs", "getSprintVelocityHandler.HandleAsync"),
            ("LifecycleFacade.cs", "createSprintHandler.HandleAsync"),
            ("LifecycleFacade.cs", "startSprintHandler.HandleAsync"),
            ("LifecycleFacade.cs", "completeSprintHandler.HandleAsync"),
            ("AssignmentFacade.cs", "planSprintWorkItemHandler.HandleAsync"),
            ("AssignmentFacade.cs", "unplanSprintWorkItemHandler.HandleAsync")
        })
        {
            Assert.Contains(
                delegation,
                File.ReadAllText(Path.Combine(facadeDirectory, file)),
                StringComparison.Ordinal);
        }

        var endpoint = File.ReadAllText(Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Presentation",
            "Endpoints",
            "WorkItems",
            "PlatformCore",
            "SprintEndpoints.cs"));
        Assert.Contains("GetSprintHandler handler", endpoint, StringComparison.Ordinal);
        Assert.Contains("ListSprintsHandler handler", endpoint, StringComparison.Ordinal);
        Assert.Contains("ListSprintBacklogHandler handler", endpoint, StringComparison.Ordinal);
        Assert.Contains("GetSprintBurndownHandler burndown", endpoint, StringComparison.Ordinal);
        Assert.Contains("GetSprintVelocityHandler handler", endpoint, StringComparison.Ordinal);
        Assert.Contains("CreateSprintHandler handler", endpoint, StringComparison.Ordinal);
        Assert.Contains("StartSprintHandler handler", endpoint, StringComparison.Ordinal);
        Assert.Contains("CompleteSprintHandler handler", endpoint, StringComparison.Ordinal);
        Assert.Contains("PlanSprintWorkItemHandler handler", endpoint, StringComparison.Ordinal);
        Assert.Contains("UnplanSprintWorkItemHandler handler", endpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("service.ListAsync(projectId", endpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("service.BacklogAsync(projectId", endpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("service.CreateAsync(request", endpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("service.StartAsync(sprintId", endpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("service.CompleteAsync(sprintId", endpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("service.PlanAsync(sprintId", endpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("service.UnplanAsync(sprintId", endpoint, StringComparison.Ordinal);
        Assert.Contains("AddScoped<GetSprintHandler>(provider =>", endpoint, StringComparison.Ordinal);
        Assert.Contains("AddScoped<ListSprintsHandler>(provider =>", endpoint, StringComparison.Ordinal);
        Assert.Contains("AddScoped<ListSprintBacklogHandler>(provider =>", endpoint, StringComparison.Ordinal);
        Assert.Contains("AddScoped<GetSprintBurndownHandler>(provider =>", endpoint, StringComparison.Ordinal);
        Assert.Contains("AddScoped<GetSprintVelocityHandler>(provider =>", endpoint, StringComparison.Ordinal);
        Assert.Contains("AddScoped<CreateSprintHandler>(provider =>", endpoint, StringComparison.Ordinal);
        Assert.Contains("AddScoped<StartSprintHandler>(provider =>", endpoint, StringComparison.Ordinal);
        Assert.Contains("AddScoped<CompleteSprintHandler>(provider =>", endpoint, StringComparison.Ordinal);
        Assert.Contains("AddScoped<PlanSprintWorkItemHandler>(provider =>", endpoint, StringComparison.Ordinal);
        Assert.Contains("AddScoped<UnplanSprintWorkItemHandler>(provider =>", endpoint, StringComparison.Ordinal);

        var reportDirectory = Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Presentation",
            "Endpoints",
            "WorkItems",
            "Sprints");
        var burndownReport = File.ReadAllText(Path.Combine(
            reportDirectory,
            "GetSprintBurndownReportEndpoint.cs"));
        Assert.Contains("GetSprintBurndownHandler handler", burndownReport, StringComparison.Ordinal);
        Assert.DoesNotContain("SprintService service", burndownReport, StringComparison.Ordinal);
        var velocityReport = File.ReadAllText(Path.Combine(
            reportDirectory,
            "GetSprintVelocityReportEndpoint.cs"));
        Assert.Contains("GetSprintVelocityHandler handler", velocityReport, StringComparison.Ordinal);
        Assert.DoesNotContain("SprintService service", velocityReport, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkItemCommentEndpoints_AreIndependentFeatureEndpointClasses()
    {
        var commentsDirectory = Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Presentation",
            "Endpoints",
            "WorkItems",
            "Comments");
        var endpointFiles = new Dictionary<string, string>
        {
            ["AddCommentEndpoint.cs"] = "internal static class AddCommentEndpoint",
            ["EditCommentEndpoint.cs"] = "internal static class EditCommentEndpoint",
            ["DeleteCommentEndpoint.cs"] = "internal static class DeleteCommentEndpoint",
            ["ListCommentsEndpoint.cs"] = "internal static class ListCommentsEndpoint",
            ["ListCommentRevisionsEndpoint.cs"] = "internal static class ListCommentRevisionsEndpoint"
        };

        foreach (var endpointFile in endpointFiles)
        {
            var source = File.ReadAllText(Path.Combine(commentsDirectory, endpointFile.Key));
            Assert.Contains(
                "namespace Zumbo.Api.Presentation.Endpoints.WorkItems.Comments;",
                source,
                StringComparison.Ordinal);
            Assert.Contains(endpointFile.Value, source, StringComparison.Ordinal);
            Assert.Contains("internal static void Map(RouteGroupBuilder group)", source, StringComparison.Ordinal);
            Assert.DoesNotContain("partial class WorkItemEndpoints", source, StringComparison.Ordinal);
        }

        var routeHost = File.ReadAllText(Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Presentation",
            "Endpoints",
            "WorkItemEndpoints",
            "WorkItemEndpoints.MapWorkItemEndpoints.cs"));
        Assert.Contains("AddCommentEndpoint.Map(group);", routeHost, StringComparison.Ordinal);
        Assert.Contains("ListCommentsEndpoint.Map(group);", routeHost, StringComparison.Ordinal);
        Assert.Contains("ListCommentRevisionsEndpoint.Map(group);", routeHost, StringComparison.Ordinal);
        Assert.Contains("EditCommentEndpoint.Map(group);", routeHost, StringComparison.Ordinal);
        Assert.Contains("DeleteCommentEndpoint.Map(group);", routeHost, StringComparison.Ordinal);

        var compatibility = File.ReadAllText(Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Presentation",
            "Endpoints",
            "WorkItemEndpoints",
            "WorkItemEndpoints.Comments.cs"));
        Assert.Contains("private static void MapPostByIdComments", compatibility, StringComparison.Ordinal);
        Assert.Contains("private static void MapGetByIdComments", compatibility, StringComparison.Ordinal);
        Assert.Contains("private static void MapGetByIdCommentsByCommentIdRevisions", compatibility, StringComparison.Ordinal);
        Assert.Contains("private static void MapPutByIdCommentsByCommentId", compatibility, StringComparison.Ordinal);
        Assert.Contains("private static void MapDeleteByIdCommentsByCommentId", compatibility, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkItemLabelEndpoints_AreIndependentFeatureEndpointClasses()
    {
        var labelsDirectory = Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Presentation",
            "Endpoints",
            "WorkItems",
            "Labels");
        var endpointFiles = new Dictionary<string, string>
        {
            ["AddLabelEndpoint.cs"] = "internal static class AddLabelEndpoint",
            ["RemoveLabelEndpoint.cs"] = "internal static class RemoveLabelEndpoint"
        };

        foreach (var endpointFile in endpointFiles)
        {
            var source = File.ReadAllText(Path.Combine(labelsDirectory, endpointFile.Key));
            Assert.Contains(
                "namespace Zumbo.Api.Presentation.Endpoints.WorkItems.Labels;",
                source,
                StringComparison.Ordinal);
            Assert.Contains(endpointFile.Value, source, StringComparison.Ordinal);
            Assert.Contains("internal static void Map(RouteGroupBuilder group)", source, StringComparison.Ordinal);
            Assert.DoesNotContain("partial class WorkItemEndpoints", source, StringComparison.Ordinal);
        }

        var routeHost = File.ReadAllText(Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Presentation",
            "Endpoints",
            "WorkItemEndpoints",
            "WorkItemEndpoints.MapWorkItemEndpoints.cs"));
        Assert.Contains("AddLabelEndpoint.Map(group);", routeHost, StringComparison.Ordinal);
        Assert.Contains("RemoveLabelEndpoint.Map(group);", routeHost, StringComparison.Ordinal);

        var compatibility = File.ReadAllText(Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Presentation",
            "Endpoints",
            "WorkItemEndpoints",
            "WorkItemEndpoints.Labels.cs"));
        Assert.Contains("private static void MapPostByIdLabels", compatibility, StringComparison.Ordinal);
        Assert.Contains("private static void MapDeleteByIdLabelsByLabel", compatibility, StringComparison.Ordinal);
        Assert.Contains("AddLabelEndpoint.Map(group);", compatibility, StringComparison.Ordinal);
        Assert.Contains("RemoveLabelEndpoint.Map(group);", compatibility, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkItemChecklistEndpoints_AreIndependentFeatureEndpointClasses()
    {
        var checklistDirectory = Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Presentation",
            "Endpoints",
            "WorkItems",
            "Checklist");
        var endpointFiles = new Dictionary<string, string>
        {
            ["AddChecklistItemEndpoint.cs"] = "internal static class AddChecklistItemEndpoint",
            ["SetChecklistItemCompletionEndpoint.cs"] = "internal static class SetChecklistItemCompletionEndpoint"
        };

        foreach (var endpointFile in endpointFiles)
        {
            var source = File.ReadAllText(Path.Combine(checklistDirectory, endpointFile.Key));
            Assert.Contains(
                "namespace Zumbo.Api.Presentation.Endpoints.WorkItems.Checklist;",
                source,
                StringComparison.Ordinal);
            Assert.Contains(endpointFile.Value, source, StringComparison.Ordinal);
            Assert.Contains("internal static void Map(RouteGroupBuilder group)", source, StringComparison.Ordinal);
            Assert.DoesNotContain("partial class WorkItemEndpoints", source, StringComparison.Ordinal);
        }

        var routeHost = File.ReadAllText(Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Presentation",
            "Endpoints",
            "WorkItemEndpoints",
            "WorkItemEndpoints.MapWorkItemEndpoints.cs"));
        Assert.Contains("AddChecklistItemEndpoint.Map(group);", routeHost, StringComparison.Ordinal);
        Assert.Contains("SetChecklistItemCompletionEndpoint.Map(group);", routeHost, StringComparison.Ordinal);

        var compatibility = File.ReadAllText(Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Presentation",
            "Endpoints",
            "WorkItemEndpoints",
            "WorkItemEndpoints.Checklist.cs"));
        Assert.Contains("private static void MapPostByIdChecklist", compatibility, StringComparison.Ordinal);
        Assert.Contains("private static void MapPatchByIdChecklistByItemId", compatibility, StringComparison.Ordinal);
        Assert.Contains("AddChecklistItemEndpoint.Map(group);", compatibility, StringComparison.Ordinal);
        Assert.Contains("SetChecklistItemCompletionEndpoint.Map(group);", compatibility, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkItemRelationEndpoints_AreIndependentFeatureEndpointClasses()
    {
        var relationsDirectory = Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Presentation",
            "Endpoints",
            "WorkItems",
            "Relations");
        var endpointFiles = new Dictionary<string, string>
        {
            ["LinkWorkItemEndpoint.cs"] = "internal static class LinkWorkItemEndpoint",
            ["UnlinkWorkItemEndpoint.cs"] = "internal static class UnlinkWorkItemEndpoint"
        };

        foreach (var endpointFile in endpointFiles)
        {
            var source = File.ReadAllText(Path.Combine(relationsDirectory, endpointFile.Key));
            Assert.Contains(
                "namespace Zumbo.Api.Presentation.Endpoints.WorkItems.Relations;",
                source,
                StringComparison.Ordinal);
            Assert.Contains(endpointFile.Value, source, StringComparison.Ordinal);
            Assert.Contains("internal static void Map(RouteGroupBuilder group)", source, StringComparison.Ordinal);
            Assert.DoesNotContain("partial class WorkItemEndpoints", source, StringComparison.Ordinal);
        }

        var routeHost = File.ReadAllText(Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Presentation",
            "Endpoints",
            "WorkItemEndpoints",
            "WorkItemEndpoints.MapWorkItemEndpoints.cs"));
        Assert.Contains("LinkWorkItemEndpoint.Map(group);", routeHost, StringComparison.Ordinal);
        Assert.Contains("UnlinkWorkItemEndpoint.Map(group);", routeHost, StringComparison.Ordinal);

        var compatibility = File.ReadAllText(Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Presentation",
            "Endpoints",
            "WorkItemEndpoints",
            "WorkItemEndpoints.Relations.cs"));
        Assert.Contains("private static void MapPostByIdRelations", compatibility, StringComparison.Ordinal);
        Assert.Contains("private static void MapDeleteByIdRelationsByRelatedWorkItemId", compatibility, StringComparison.Ordinal);
        Assert.Contains("LinkWorkItemEndpoint.Map(group);", compatibility, StringComparison.Ordinal);
        Assert.Contains("UnlinkWorkItemEndpoint.Map(group);", compatibility, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkItemWorklogEndpoints_AreIndependentFeatureEndpointClasses()
    {
        var worklogsDirectory = Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Presentation",
            "Endpoints",
            "WorkItems",
            "Worklogs");
        var endpointFiles = new Dictionary<string, string>
        {
            ["AddWorkLogEndpoint.cs"] = "internal static class AddWorkLogEndpoint",
            ["ListWorkLogsEndpoint.cs"] = "internal static class ListWorkLogsEndpoint"
        };

        foreach (var endpointFile in endpointFiles)
        {
            var source = File.ReadAllText(Path.Combine(worklogsDirectory, endpointFile.Key));
            Assert.Contains(
                "namespace Zumbo.Api.Presentation.Endpoints.WorkItems.Worklogs;",
                source,
                StringComparison.Ordinal);
            Assert.Contains(endpointFile.Value, source, StringComparison.Ordinal);
            Assert.Contains("internal static void Map(RouteGroupBuilder group)", source, StringComparison.Ordinal);
            Assert.DoesNotContain("partial class WorkItemEndpoints", source, StringComparison.Ordinal);
        }

        var routeHost = File.ReadAllText(Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Presentation",
            "Endpoints",
            "WorkItemEndpoints",
            "WorkItemEndpoints.MapWorkItemEndpoints.cs"));
        Assert.Contains("AddWorkLogEndpoint.Map(group);", routeHost, StringComparison.Ordinal);
        Assert.Contains("ListWorkLogsEndpoint.Map(group);", routeHost, StringComparison.Ordinal);

        var compatibility = File.ReadAllText(Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Presentation",
            "Endpoints",
            "WorkItemEndpoints",
            "WorkItemEndpoints.Worklogs.cs"));
        Assert.Contains("private static void MapPostByIdWorklogs", compatibility, StringComparison.Ordinal);
        Assert.Contains("private static void MapGetByIdWorklogs", compatibility, StringComparison.Ordinal);
        Assert.Contains("AddWorkLogEndpoint.Map(group);", compatibility, StringComparison.Ordinal);
        Assert.Contains("ListWorkLogsEndpoint.Map(group);", compatibility, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkItemAttachmentEndpoints_AreIndependentFeatureEndpointClasses()
    {
        var directory = Path.Combine(SourceDirectory, "Zumbo.Api", "Presentation", "Endpoints", "WorkItems", "Attachments");
        var endpointFiles = new Dictionary<string, string>
        {
            ["UploadAttachmentEndpoint.cs"] = "UploadAttachmentEndpoint",
            ["ListAttachmentsEndpoint.cs"] = "ListAttachmentsEndpoint",
            ["DownloadAttachmentEndpoint.cs"] = "DownloadAttachmentEndpoint",
            ["PreviewAttachmentEndpoint.cs"] = "PreviewAttachmentEndpoint",
            ["DeleteAttachmentEndpoint.cs"] = "DeleteAttachmentEndpoint"
        };

        foreach (var endpointFile in endpointFiles)
        {
            var source = File.ReadAllText(Path.Combine(directory, endpointFile.Key));
            Assert.Contains("namespace Zumbo.Api.Presentation.Endpoints.WorkItems.Attachments;", source, StringComparison.Ordinal);
            Assert.Contains($"internal static class {endpointFile.Value}", source, StringComparison.Ordinal);
            Assert.Contains("internal static void Map(RouteGroupBuilder group)", source, StringComparison.Ordinal);
            Assert.DoesNotContain("partial class WorkItemEndpoints", source, StringComparison.Ordinal);
        }

        var routeHost = File.ReadAllText(Path.Combine(SourceDirectory, "Zumbo.Api", "Presentation", "Endpoints", "WorkItemEndpoints", "WorkItemEndpoints.MapWorkItemEndpoints.cs"));
        foreach (var endpointClass in endpointFiles.Values)
        {
            Assert.Contains($"{endpointClass}.Map(group);", routeHost, StringComparison.Ordinal);
        }

        var compatibility = File.ReadAllText(Path.Combine(SourceDirectory, "Zumbo.Api", "Presentation", "Endpoints", "WorkItemEndpoints", "WorkItemEndpoints.Attachments.cs"));
        Assert.Contains("MapPostByIdAttachmentsUpload", compatibility, StringComparison.Ordinal);
        Assert.Contains("MapGetByIdAttachments", compatibility, StringComparison.Ordinal);
        Assert.Contains("MapGetByIdAttachmentsByAttachmentIdDownload", compatibility, StringComparison.Ordinal);
        Assert.Contains("MapGetByIdAttachmentsByAttachmentIdPreview", compatibility, StringComparison.Ordinal);
        Assert.Contains("MapDeleteByIdAttachmentsByAttachmentId", compatibility, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkItemApprovalEndpoints_AreIndependentFeatureEndpointClasses()
    {
        var directory = Path.Combine(SourceDirectory, "Zumbo.Api", "Presentation", "Endpoints", "WorkItems", "Approvals");
        var endpointFiles = new Dictionary<string, string>
        {
            ["RequestApprovalEndpoint.cs"] = "RequestApprovalEndpoint",
            ["DecideApprovalEndpoint.cs"] = "DecideApprovalEndpoint",
            ["ListApprovalsEndpoint.cs"] = "ListApprovalsEndpoint"
        };

        foreach (var endpointFile in endpointFiles)
        {
            var source = File.ReadAllText(Path.Combine(directory, endpointFile.Key));
            Assert.Contains("namespace Zumbo.Api.Presentation.Endpoints.WorkItems.Approvals;", source, StringComparison.Ordinal);
            Assert.Contains($"internal static class {endpointFile.Value}", source, StringComparison.Ordinal);
            Assert.Contains("internal static void Map(RouteGroupBuilder group)", source, StringComparison.Ordinal);
            Assert.DoesNotContain("partial class WorkItemEndpoints", source, StringComparison.Ordinal);
        }

        var routeHost = File.ReadAllText(Path.Combine(SourceDirectory, "Zumbo.Api", "Presentation", "Endpoints", "WorkItemEndpoints", "WorkItemEndpoints.MapWorkItemEndpoints.cs"));
        foreach (var endpointClass in endpointFiles.Values)
        {
            Assert.Contains($"{endpointClass}.Map(group);", routeHost, StringComparison.Ordinal);
        }

        var compatibility = File.ReadAllText(Path.Combine(SourceDirectory, "Zumbo.Api", "Presentation", "Endpoints", "WorkItemEndpoints", "WorkItemEndpoints.Approvals.cs"));
        Assert.Contains("MapPostByIdApprovals", compatibility, StringComparison.Ordinal);
        Assert.Contains("MapPostByIdApprovalsByApprovalIdDecision", compatibility, StringComparison.Ordinal);
        Assert.Contains("MapGetByIdApprovals", compatibility, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkItemSchemaAndDurableMessagingEndpoints_AreIndependentFeatureEndpointClasses()
    {
        var endpointFiles = new Dictionary<string, (string Directory, string Namespace, string ClassName)>
        {
            ["ListDurableMessageDeadLettersEndpoint.cs"] = ("WorkItemsCore", "Zumbo.Api.Presentation.Endpoints.WorkItems.WorkItemsCore", "ListDurableMessageDeadLettersEndpoint"),
            ["ReplayDurableMessageDeadLetterEndpoint.cs"] = ("WorkItemsCore", "Zumbo.Api.Presentation.Endpoints.WorkItems.WorkItemsCore", "ReplayDurableMessageDeadLetterEndpoint"),
            ["GetDurableMessagingMetricsEndpoint.cs"] = ("Reports", "Zumbo.Api.Presentation.Endpoints.WorkItems.Reports", "GetDurableMessagingMetricsEndpoint"),
            ["SetWorkItemCustomFieldsEndpoint.cs"] = ("Schema", "Zumbo.Api.Presentation.Endpoints.WorkItems.Schema", "SetWorkItemCustomFieldsEndpoint")
        };

        foreach (var endpointFile in endpointFiles)
        {
            var source = File.ReadAllText(Path.Combine(
                SourceDirectory,
                "Zumbo.Api",
                "Presentation",
                "Endpoints",
                "WorkItems",
                endpointFile.Value.Directory,
                endpointFile.Key));
            Assert.Contains($"namespace {endpointFile.Value.Namespace};", source, StringComparison.Ordinal);
            Assert.Contains($"internal static class {endpointFile.Value.ClassName}", source, StringComparison.Ordinal);
            Assert.Contains("internal static void Map(RouteGroupBuilder group)", source, StringComparison.Ordinal);
            Assert.DoesNotContain("partial class WorkItemEndpoints", source, StringComparison.Ordinal);
        }

        var routeHost = File.ReadAllText(Path.Combine(SourceDirectory, "Zumbo.Api", "Presentation", "Endpoints", "WorkItemEndpoints", "WorkItemEndpoints.MapWorkItemEndpoints.cs"));
        foreach (var endpointClass in endpointFiles.Values.Select(value => value.ClassName))
        {
            Assert.Contains($"{endpointClass}.Map(group);", routeHost, StringComparison.Ordinal);
        }

        var durableCompatibility = File.ReadAllText(Path.Combine(SourceDirectory, "Zumbo.Api", "Presentation", "Endpoints", "WorkItemEndpoints", "WorkItemEndpoints.DurableMessaging.cs"));
        Assert.Contains("MapGetDurableMessagingMetrics", durableCompatibility, StringComparison.Ordinal);
        Assert.Contains("MapGetDurableMessagingDeadLetters", durableCompatibility, StringComparison.Ordinal);
        Assert.Contains("MapPostDurableMessagingDeadLetterByMessageIdReplay", durableCompatibility, StringComparison.Ordinal);

        var schemaCompatibility = File.ReadAllText(Path.Combine(SourceDirectory, "Zumbo.Api", "Presentation", "Endpoints", "WorkItemEndpoints", "WorkItemEndpoints.Schema.cs"));
        Assert.Contains("MapPutByIdCustomFields", schemaCompatibility, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkItemEndpointHost_ContainsOnlyApprovedHostHelpersAndCompatibilityFacades()
    {
        var endpointRoot = Path.Combine(SourceDirectory, "Zumbo.Api", "Presentation", "Endpoints");
        var hostDirectory = Path.Combine(endpointRoot, "WorkItemEndpoints");
        var hostAndHelperFiles = new[]
        {
            "WorkItemEndpoints.AddWorkItemsModule.cs",
            "WorkItemEndpoints.IdempotencyKey.cs",
            "WorkItemEndpoints.MapWorkItemEndpoints.cs",
            "WorkItemEndpoints.ReportOk.cs"
        };
        var compatibilityFiles = new[]
        {
            "WorkItemEndpoints.Activity.cs",
            "WorkItemEndpoints.Approvals.cs",
            "WorkItemEndpoints.Attachments.cs",
            "WorkItemEndpoints.BulkOperations.cs",
            "WorkItemEndpoints.Checklist.cs",
            "WorkItemEndpoints.Comments.cs",
            "WorkItemEndpoints.DurableMessaging.cs",
            "WorkItemEndpoints.Labels.cs",
            "WorkItemEndpoints.Planning.cs",
            "WorkItemEndpoints.Realtime.cs",
            "WorkItemEndpoints.Recurrences.cs",
            "WorkItemEndpoints.Relations.cs",
            "WorkItemEndpoints.Reports.cs",
            "WorkItemEndpoints.Schema.cs",
            "WorkItemEndpoints.Search.cs",
            "WorkItemEndpoints.WorkItemsCore.cs",
            "WorkItemEndpoints.Worklogs.cs"
        };
        var expectedFiles = hostAndHelperFiles
            .Concat(compatibilityFiles)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var actualFiles = Directory.GetFiles(hostDirectory, "*.cs", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expectedFiles, actualFiles);
        Assert.False(File.Exists(Path.Combine(endpointRoot, "WorkItemEndpoints.cs")));
        Assert.Empty(Directory.Exists(Path.Combine(hostDirectory, "MapWorkItemEndpoints"))
            ? Directory.GetFiles(Path.Combine(hostDirectory, "MapWorkItemEndpoints"), "*.cs", SearchOption.AllDirectories)
            : []);

        foreach (var compatibilityFile in compatibilityFiles)
        {
            var source = File.ReadAllText(Path.Combine(hostDirectory, compatibilityFile));
            Assert.Contains("partial class WorkItemEndpoints", source, StringComparison.Ordinal);
            Assert.DoesNotContain("group.Map", source, StringComparison.Ordinal);
        }

        var routeHost = File.ReadAllText(Path.Combine(
            hostDirectory,
            "WorkItemEndpoints.MapWorkItemEndpoints.cs"));
        var featureEndpointClasses = Directory
            .GetFiles(Path.Combine(endpointRoot, "WorkItems"), "*.cs", SearchOption.AllDirectories)
            .SelectMany(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path))
                .GetRoot()
                .DescendantNodes()
                .OfType<ClassDeclarationSyntax>())
            .Where(type => type.Identifier.ValueText.EndsWith("Endpoint", StringComparison.Ordinal))
            .Where(type => type.Members
                .OfType<MethodDeclarationSyntax>()
                .Any(method => method.Identifier.ValueText == "Map"))
            .Select(type => type.Identifier.ValueText)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(featureEndpointClasses);
        Assert.All(
            featureEndpointClasses,
            endpointClass => Assert.Contains($"{endpointClass}.Map(group);", routeHost, StringComparison.Ordinal));

        var allowList = File.ReadAllText(Path.GetFullPath(Path.Combine(
            BackendDirectory,
            "..",
            "docs",
            "architecture",
            "module-first-architecture-allowlist.json")));
        Assert.DoesNotContain("WorkItemEndpoints/MapWorkItemEndpoints/", allowList, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkItemReportEndpoints_AreIndependentFeatureEndpointClasses()
    {
        var reportDirectory = Path.Combine(SourceDirectory, "Zumbo.Api", "Presentation", "Endpoints", "WorkItems", "Reports");
        var reportEndpointFiles = new Dictionary<string, string>
        {
            ["GetProjectSummaryReportEndpoint.cs"] = "GetProjectSummaryReportEndpoint",
            ["GetStatusDistributionReportEndpoint.cs"] = "GetStatusDistributionReportEndpoint",
            ["GetUserWorkloadReportEndpoint.cs"] = "GetUserWorkloadReportEndpoint",
            ["GetDueDateRisksReportEndpoint.cs"] = "GetDueDateRisksReportEndpoint",
            ["GetFlowTimeReportEndpoint.cs"] = "GetFlowTimeReportEndpoint",
            ["GetCompletionRateReportEndpoint.cs"] = "GetCompletionRateReportEndpoint",
            ["GetTeamPerformanceReportEndpoint.cs"] = "GetTeamPerformanceReportEndpoint"
        };
        var sprintDirectory = Path.Combine(SourceDirectory, "Zumbo.Api", "Presentation", "Endpoints", "WorkItems", "Sprints");
        var sprintEndpointFiles = new Dictionary<string, string>
        {
            ["GetSprintBurndownReportEndpoint.cs"] = "GetSprintBurndownReportEndpoint",
            ["GetSprintVelocityReportEndpoint.cs"] = "GetSprintVelocityReportEndpoint"
        };

        AssertEndpointClasses(reportDirectory, reportEndpointFiles, "Zumbo.Api.Presentation.Endpoints.WorkItems.Reports");
        AssertEndpointClasses(sprintDirectory, sprintEndpointFiles, "Zumbo.Api.Presentation.Endpoints.WorkItems.Sprints");

        var routeHost = File.ReadAllText(Path.Combine(SourceDirectory, "Zumbo.Api", "Presentation", "Endpoints", "WorkItemEndpoints", "WorkItemEndpoints.MapWorkItemEndpoints.cs"));
        foreach (var endpointClass in reportEndpointFiles.Values.Concat(sprintEndpointFiles.Values))
        {
            Assert.Contains($"{endpointClass}.Map(group);", routeHost, StringComparison.Ordinal);
        }

        var compatibility = File.ReadAllText(Path.Combine(SourceDirectory, "Zumbo.Api", "Presentation", "Endpoints", "WorkItemEndpoints", "WorkItemEndpoints.Reports.cs"));
        Assert.Contains("MapGetReportsProjectSummaryByProjectId", compatibility, StringComparison.Ordinal);
        Assert.Contains("MapGetReportsStatusDistributionByProjectId", compatibility, StringComparison.Ordinal);
        Assert.Contains("MapGetReportsUserWorkloadByProjectId", compatibility, StringComparison.Ordinal);
        Assert.Contains("MapGetReportsDueDateRisksByProjectId", compatibility, StringComparison.Ordinal);
        Assert.Contains("MapGetReportsFlowTimeByProjectId", compatibility, StringComparison.Ordinal);
        Assert.Contains("MapGetReportsCompletionRateByProjectId", compatibility, StringComparison.Ordinal);
        Assert.Contains("MapGetReportsTeamPerformanceByProjectId", compatibility, StringComparison.Ordinal);
        Assert.Contains("MapGetReportsSprintBurndownByProjectIdBySprintId", compatibility, StringComparison.Ordinal);
        Assert.Contains("MapGetReportsSprintVelocityByProjectId", compatibility, StringComparison.Ordinal);

        static void AssertEndpointClasses(string directory, IReadOnlyDictionary<string, string> endpointFiles, string endpointNamespace)
        {
            foreach (var endpointFile in endpointFiles)
            {
                var source = File.ReadAllText(Path.Combine(directory, endpointFile.Key));
                Assert.Contains($"namespace {endpointNamespace};", source, StringComparison.Ordinal);
                Assert.Contains($"internal static class {endpointFile.Value}", source, StringComparison.Ordinal);
                Assert.Contains("internal static void Map(RouteGroupBuilder group)", source, StringComparison.Ordinal);
                Assert.DoesNotContain("partial class WorkItemEndpoints", source, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void WorkItemSearchEndpoints_AreIndependentFeatureEndpointClasses()
    {
        var directory = Path.Combine(SourceDirectory, "Zumbo.Api", "Presentation", "Endpoints", "WorkItems", "Search");
        var endpointFiles = new Dictionary<string, string>
        {
            ["SearchWorkItemsPageEndpoint.cs"] = "SearchWorkItemsPageEndpoint",
            ["RebuildSearchIndexEndpoint.cs"] = "RebuildSearchIndexEndpoint",
            ["ReconcileSearchIndexEndpoint.cs"] = "ReconcileSearchIndexEndpoint"
        };

        foreach (var endpointFile in endpointFiles)
        {
            var source = File.ReadAllText(Path.Combine(directory, endpointFile.Key));
            Assert.Contains("namespace Zumbo.Api.Presentation.Endpoints.WorkItems.Search;", source, StringComparison.Ordinal);
            Assert.Contains($"internal static class {endpointFile.Value}", source, StringComparison.Ordinal);
            Assert.Contains("internal static void Map(RouteGroupBuilder group)", source, StringComparison.Ordinal);
            Assert.DoesNotContain("partial class WorkItemEndpoints", source, StringComparison.Ordinal);
        }

        var routeHost = File.ReadAllText(Path.Combine(SourceDirectory, "Zumbo.Api", "Presentation", "Endpoints", "WorkItemEndpoints", "WorkItemEndpoints.MapWorkItemEndpoints.cs"));
        foreach (var endpointClass in endpointFiles.Values)
        {
            Assert.Contains($"{endpointClass}.Map(group);", routeHost, StringComparison.Ordinal);
        }

        var compatibility = File.ReadAllText(Path.Combine(SourceDirectory, "Zumbo.Api", "Presentation", "Endpoints", "WorkItemEndpoints", "WorkItemEndpoints.Search.cs"));
        Assert.Contains("MapPostSearch", compatibility, StringComparison.Ordinal);
        Assert.Contains("MapPostSearchRebuild", compatibility, StringComparison.Ordinal);
        Assert.Contains("MapPostSearchReconcile", compatibility, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkItemRecurrenceEndpoints_AreIndependentFeatureEndpointClasses()
    {
        var directory = Path.Combine(SourceDirectory, "Zumbo.Api", "Presentation", "Endpoints", "WorkItems", "Recurrences");
        var endpointFiles = new Dictionary<string, string>
        {
            ["ListRecurrencesEndpoint.cs"] = "ListRecurrencesEndpoint",
            ["CreateRecurrenceEndpoint.cs"] = "CreateRecurrenceEndpoint",
            ["DeleteRecurrenceEndpoint.cs"] = "DeleteRecurrenceEndpoint",
            ["PreviewRecurrenceEndpoint.cs"] = "PreviewRecurrenceEndpoint",
            ["ProcessDueRecurrencesEndpoint.cs"] = "ProcessDueRecurrencesEndpoint",
            ["SetRecurrenceStateEndpoint.cs"] = "SetRecurrenceStateEndpoint",
            ["ListRecurrenceOccurrencesEndpoint.cs"] = "ListRecurrenceOccurrencesEndpoint",
            ["ListTemplatesEndpoint.cs"] = "ListTemplatesEndpoint",
            ["CreateTemplateEndpoint.cs"] = "CreateTemplateEndpoint",
            ["UpdateTemplateEndpoint.cs"] = "UpdateTemplateEndpoint",
            ["DeleteTemplateEndpoint.cs"] = "DeleteTemplateEndpoint"
        };

        foreach (var endpointFile in endpointFiles)
        {
            var source = File.ReadAllText(Path.Combine(directory, endpointFile.Key));
            Assert.Contains("namespace Zumbo.Api.Presentation.Endpoints.WorkItems.Recurrences;", source, StringComparison.Ordinal);
            Assert.Contains($"internal static class {endpointFile.Value}", source, StringComparison.Ordinal);
            Assert.Contains("internal static void Map(RouteGroupBuilder group)", source, StringComparison.Ordinal);
            Assert.DoesNotContain("partial class WorkItemEndpoints", source, StringComparison.Ordinal);
        }

        var routeHost = File.ReadAllText(Path.Combine(SourceDirectory, "Zumbo.Api", "Presentation", "Endpoints", "WorkItemEndpoints", "WorkItemEndpoints.MapWorkItemEndpoints.cs"));
        foreach (var endpointClass in endpointFiles.Values)
        {
            Assert.Contains($"{endpointClass}.Map(group);", routeHost, StringComparison.Ordinal);
        }

        var compatibility = File.ReadAllText(Path.Combine(SourceDirectory, "Zumbo.Api", "Presentation", "Endpoints", "WorkItemEndpoints", "WorkItemEndpoints.Recurrences.cs"));
        Assert.Contains("MapGetRecurrences", compatibility, StringComparison.Ordinal);
        Assert.Contains("MapPostRecurrences", compatibility, StringComparison.Ordinal);
        Assert.Contains("MapDeleteRecurrencesByRecurrenceId", compatibility, StringComparison.Ordinal);
        Assert.Contains("MapPostRecurrencesPreview", compatibility, StringComparison.Ordinal);
        Assert.Contains("MapPostRecurrencesProcessDue", compatibility, StringComparison.Ordinal);
        Assert.Contains("MapPatchRecurrencesByRecurrenceIdState", compatibility, StringComparison.Ordinal);
        Assert.Contains("MapGetRecurrencesByRecurrenceIdOccurrences", compatibility, StringComparison.Ordinal);
        Assert.Contains("MapGetTemplates", compatibility, StringComparison.Ordinal);
        Assert.Contains("MapPostTemplates", compatibility, StringComparison.Ordinal);
        Assert.Contains("MapPutTemplatesByTemplateId", compatibility, StringComparison.Ordinal);
        Assert.Contains("MapDeleteTemplatesByTemplateId", compatibility, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkItemBulkOperationEndpoints_AreIndependentFeatureEndpointClasses()
    {
        var directory = Path.Combine(SourceDirectory, "Zumbo.Api", "Presentation", "Endpoints", "WorkItems", "BulkOperations");
        var endpointFiles = new Dictionary<string, string>
        {
            ["BulkMoveWorkItemsEndpoint.cs"] = "BulkMoveWorkItemsEndpoint",
            ["BulkAssignWorkItemsEndpoint.cs"] = "BulkAssignWorkItemsEndpoint",
            ["BulkArchiveWorkItemsEndpoint.cs"] = "BulkArchiveWorkItemsEndpoint",
            ["CreateBulkJobEndpoint.cs"] = "CreateBulkJobEndpoint",
            ["ListBulkJobsEndpoint.cs"] = "ListBulkJobsEndpoint",
            ["GetBulkJobEndpoint.cs"] = "GetBulkJobEndpoint",
            ["ListBulkJobErrorsEndpoint.cs"] = "ListBulkJobErrorsEndpoint",
            ["GetBulkJobResultEndpoint.cs"] = "GetBulkJobResultEndpoint",
            ["CancelBulkJobEndpoint.cs"] = "CancelBulkJobEndpoint",
            ["RetryBulkJobEndpoint.cs"] = "RetryBulkJobEndpoint",
            ["CreateBulkExportJobEndpoint.cs"] = "CreateBulkExportJobEndpoint",
            ["CreateBulkImportJobEndpoint.cs"] = "CreateBulkImportJobEndpoint"
        };

        foreach (var endpointFile in endpointFiles)
        {
            var source = File.ReadAllText(Path.Combine(directory, endpointFile.Key));
            Assert.Contains("namespace Zumbo.Api.Presentation.Endpoints.WorkItems.BulkOperations;", source, StringComparison.Ordinal);
            Assert.Contains($"internal static class {endpointFile.Value}", source, StringComparison.Ordinal);
            Assert.Contains("internal static void Map(RouteGroupBuilder group)", source, StringComparison.Ordinal);
            Assert.DoesNotContain("partial class WorkItemEndpoints", source, StringComparison.Ordinal);
        }

        var routeHost = File.ReadAllText(Path.Combine(SourceDirectory, "Zumbo.Api", "Presentation", "Endpoints", "WorkItemEndpoints", "WorkItemEndpoints.MapWorkItemEndpoints.cs"));
        foreach (var endpointClass in endpointFiles.Values)
        {
            Assert.Contains($"{endpointClass}.Map(group);", routeHost, StringComparison.Ordinal);
        }

        var compatibility = File.ReadAllText(Path.Combine(SourceDirectory, "Zumbo.Api", "Presentation", "Endpoints", "WorkItemEndpoints", "WorkItemEndpoints.BulkOperations.cs"));
        Assert.Contains("MapPostBulkMove", compatibility, StringComparison.Ordinal);
        Assert.Contains("MapPostBulkAssign", compatibility, StringComparison.Ordinal);
        Assert.Contains("MapPostBulkArchive", compatibility, StringComparison.Ordinal);
        Assert.Contains("MapPostBulkJobs", compatibility, StringComparison.Ordinal);
        Assert.Contains("MapGetBulkJobs", compatibility, StringComparison.Ordinal);
        Assert.Contains("MapGetBulkJobsByJobId", compatibility, StringComparison.Ordinal);
        Assert.Contains("MapGetBulkJobsByJobIdErrors", compatibility, StringComparison.Ordinal);
        Assert.Contains("MapGetBulkJobsByJobIdResult", compatibility, StringComparison.Ordinal);
        Assert.Contains("MapPostBulkJobsByJobIdCancel", compatibility, StringComparison.Ordinal);
        Assert.Contains("MapPostBulkJobsByJobIdRetry", compatibility, StringComparison.Ordinal);
        Assert.Contains("MapPostBulkJobsExport", compatibility, StringComparison.Ordinal);
        Assert.Contains("MapPostBulkJobsImport", compatibility, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkItemRealtimeEndpoints_AreIndependentFeatureEndpointClasses()
    {
        var directory = Path.Combine(SourceDirectory, "Zumbo.Api", "Presentation", "Endpoints", "WorkItems", "Realtime");
        var endpointFiles = new Dictionary<string, string>
        {
            ["GetCollaborationEndpoint.cs"] = "GetCollaborationEndpoint",
            ["SetVoteEndpoint.cs"] = "SetVoteEndpoint",
            ["SetWatchEndpoint.cs"] = "SetWatchEndpoint"
        };

        foreach (var endpointFile in endpointFiles)
        {
            var source = File.ReadAllText(Path.Combine(directory, endpointFile.Key));
            Assert.Contains("namespace Zumbo.Api.Presentation.Endpoints.WorkItems.Realtime;", source, StringComparison.Ordinal);
            Assert.Contains($"internal static class {endpointFile.Value}", source, StringComparison.Ordinal);
            Assert.Contains("internal static void Map(RouteGroupBuilder group)", source, StringComparison.Ordinal);
            Assert.DoesNotContain("partial class WorkItemEndpoints", source, StringComparison.Ordinal);
        }

        var routeHost = File.ReadAllText(Path.Combine(SourceDirectory, "Zumbo.Api", "Presentation", "Endpoints", "WorkItemEndpoints", "WorkItemEndpoints.MapWorkItemEndpoints.cs"));
        foreach (var endpointClass in endpointFiles.Values)
        {
            Assert.Contains($"{endpointClass}.Map(group);", routeHost, StringComparison.Ordinal);
        }

        var compatibility = File.ReadAllText(Path.Combine(SourceDirectory, "Zumbo.Api", "Presentation", "Endpoints", "WorkItemEndpoints", "WorkItemEndpoints.Realtime.cs"));
        Assert.Contains("MapGetByIdCollaboration", compatibility, StringComparison.Ordinal);
        Assert.Contains("MapPutByIdVote", compatibility, StringComparison.Ordinal);
        Assert.Contains("MapPutByIdWatch", compatibility, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkItemActivityEndpoints_AreIndependentFeatureEndpointClasses()
    {
        var directory = Path.Combine(SourceDirectory, "Zumbo.Api", "Presentation", "Endpoints", "WorkItems", "Activity");
        var endpointFiles = new Dictionary<string, string>
        {
            ["GetWorkItemActivityEndpoint.cs"] = "GetWorkItemActivityEndpoint",
            ["GetWorkItemTimelineEndpoint.cs"] = "GetWorkItemTimelineEndpoint"
        };

        foreach (var endpointFile in endpointFiles)
        {
            var source = File.ReadAllText(Path.Combine(directory, endpointFile.Key));
            Assert.Contains("namespace Zumbo.Api.Presentation.Endpoints.WorkItems.Activity;", source, StringComparison.Ordinal);
            Assert.Contains($"internal static class {endpointFile.Value}", source, StringComparison.Ordinal);
            Assert.Contains("internal static void Map(RouteGroupBuilder group)", source, StringComparison.Ordinal);
            Assert.DoesNotContain("partial class WorkItemEndpoints", source, StringComparison.Ordinal);
        }

        var routeHost = File.ReadAllText(Path.Combine(SourceDirectory, "Zumbo.Api", "Presentation", "Endpoints", "WorkItemEndpoints", "WorkItemEndpoints.MapWorkItemEndpoints.cs"));
        foreach (var endpointClass in endpointFiles.Values)
        {
            Assert.Contains($"{endpointClass}.Map(group);", routeHost, StringComparison.Ordinal);
        }

        var compatibility = File.ReadAllText(Path.Combine(SourceDirectory, "Zumbo.Api", "Presentation", "Endpoints", "WorkItemEndpoints", "WorkItemEndpoints.Activity.cs"));
        Assert.Contains("MapGetByIdActivity", compatibility, StringComparison.Ordinal);
        Assert.Contains("MapGetByIdTimeline", compatibility, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkItemPlanningEndpoints_AreIndependentFeatureEndpointClasses()
    {
        var directory = Path.Combine(SourceDirectory, "Zumbo.Api", "Presentation", "Endpoints", "WorkItems", "Planning");
        var endpointFiles = new Dictionary<string, string>
        {
            ["AssignWorkItemEndpoint.cs"] = "AssignWorkItemEndpoint",
            ["ReorderWorkItemEndpoint.cs"] = "ReorderWorkItemEndpoint",
            ["SetParentEndpoint.cs"] = "SetParentEndpoint",
            ["SetPlanningEndpoint.cs"] = "SetPlanningEndpoint",
            ["SetTeamEndpoint.cs"] = "SetTeamEndpoint"
        };

        foreach (var endpointFile in endpointFiles)
        {
            var source = File.ReadAllText(Path.Combine(directory, endpointFile.Key));
            Assert.Contains("namespace Zumbo.Api.Presentation.Endpoints.WorkItems.Planning;", source, StringComparison.Ordinal);
            Assert.Contains($"internal static class {endpointFile.Value}", source, StringComparison.Ordinal);
            Assert.Contains("internal static void Map(RouteGroupBuilder group)", source, StringComparison.Ordinal);
            Assert.DoesNotContain("partial class WorkItemEndpoints", source, StringComparison.Ordinal);
        }

        var routeHost = File.ReadAllText(Path.Combine(SourceDirectory, "Zumbo.Api", "Presentation", "Endpoints", "WorkItemEndpoints", "WorkItemEndpoints.MapWorkItemEndpoints.cs"));
        foreach (var endpointClass in endpointFiles.Values)
        {
            Assert.Contains($"{endpointClass}.Map(group);", routeHost, StringComparison.Ordinal);
        }

        var compatibility = File.ReadAllText(Path.Combine(SourceDirectory, "Zumbo.Api", "Presentation", "Endpoints", "WorkItemEndpoints", "WorkItemEndpoints.Planning.cs"));
        Assert.Contains("MapPatchByIdAssignee", compatibility, StringComparison.Ordinal);
        Assert.Contains("MapPatchByIdParent", compatibility, StringComparison.Ordinal);
        Assert.Contains("MapPatchByIdPlanning", compatibility, StringComparison.Ordinal);
        Assert.Contains("MapPatchByIdRank", compatibility, StringComparison.Ordinal);
        Assert.Contains("MapPatchByIdTeam", compatibility, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkItemCoreEndpoints_AreIndependentFeatureEndpointClasses()
    {
        var directory = Path.Combine(SourceDirectory, "Zumbo.Api", "Presentation", "Endpoints", "WorkItems", "WorkItemsCore");
        var endpointFiles = new Dictionary<string, string>
        {
            ["ArchiveWorkItemEndpoint.cs"] = "ArchiveWorkItemEndpoint",
            ["CreateWorkItemEndpoint.cs"] = "CreateWorkItemEndpoint",
            ["GetWorkItemEndpoint.cs"] = "GetWorkItemEndpoint",
            ["MoveWorkItemEndpoint.cs"] = "MoveWorkItemEndpoint",
            ["RestoreWorkItemEndpoint.cs"] = "RestoreWorkItemEndpoint",
            ["SearchWorkItemsEndpoint.cs"] = "SearchWorkItemsEndpoint",
            ["UpdateWorkItemEndpoint.cs"] = "UpdateWorkItemEndpoint"
        };

        foreach (var endpointFile in endpointFiles)
        {
            var source = File.ReadAllText(Path.Combine(directory, endpointFile.Key));
            Assert.Contains("namespace Zumbo.Api.Presentation.Endpoints.WorkItems.WorkItemsCore;", source, StringComparison.Ordinal);
            Assert.Contains($"internal static class {endpointFile.Value}", source, StringComparison.Ordinal);
            Assert.Contains("internal static void Map(RouteGroupBuilder group)", source, StringComparison.Ordinal);
            Assert.DoesNotContain("partial class WorkItemEndpoints", source, StringComparison.Ordinal);
        }

        var routeHost = File.ReadAllText(Path.Combine(SourceDirectory, "Zumbo.Api", "Presentation", "Endpoints", "WorkItemEndpoints", "WorkItemEndpoints.MapWorkItemEndpoints.cs"));
        foreach (var endpointClass in endpointFiles.Values)
        {
            Assert.Contains($"{endpointClass}.Map(group);", routeHost, StringComparison.Ordinal);
        }

        var compatibility = File.ReadAllText(Path.Combine(SourceDirectory, "Zumbo.Api", "Presentation", "Endpoints", "WorkItemEndpoints", "WorkItemEndpoints.WorkItemsCore.cs"));
        Assert.Contains("MapDeleteById", compatibility, StringComparison.Ordinal);
        Assert.Contains("MapGetById", compatibility, StringComparison.Ordinal);
        Assert.Contains("MapGetRoot", compatibility, StringComparison.Ordinal);
        Assert.Contains("MapPatchByIdStatus", compatibility, StringComparison.Ordinal);
        Assert.Contains("MapPostByIdRestore", compatibility, StringComparison.Ordinal);
        Assert.Contains("MapPostRoot", compatibility, StringComparison.Ordinal);
        Assert.Contains("MapPutById", compatibility, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkItemBulkOperations_AreFeatureHandlersWithCompatibilityFacades()
    {
        var moduleDirectory = Path.Combine(SourceDirectory, "Zumbo.Modules.WorkItems");
        var bulkDirectory = Path.Combine(moduleDirectory, "Application", "Features", "BulkOperations");
        foreach (var feature in new[] { "Move", "Assign", "Archive" })
        {
            Assert.True(File.Exists(Path.Combine(bulkDirectory, feature, $"Bulk{feature}WorkItemsCommand.cs")));
            Assert.True(File.Exists(Path.Combine(bulkDirectory, feature, $"Bulk{feature}WorkItemsHandler.cs")));
        }

        Assert.True(File.Exists(Path.Combine(bulkDirectory, "BulkWorkItemExecutor.cs")));

        var compatibilityFacade = File.ReadAllText(Path.Combine(
            moduleDirectory,
            "Application",
            "Compatibility",
            "WorkItemService",
            "WorkItemService.Lifecycle.cs"));
        Assert.Contains("new BulkMoveWorkItemsHandler(moveWorkItemHandler)", compatibilityFacade, StringComparison.Ordinal);
        Assert.Contains("new BulkAssignWorkItemsHandler(assignWorkItemHandler)", compatibilityFacade, StringComparison.Ordinal);
        Assert.Contains("new BulkArchiveWorkItemsHandler(archiveWorkItemHandler)", compatibilityFacade, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteBulkAsync(", compatibilityFacade, StringComparison.Ordinal);

        var endpointDirectory = Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Presentation",
            "Endpoints",
            "WorkItems",
            "BulkOperations");
        foreach (var feature in new[] { "Move", "Assign", "Archive" })
        {
            var endpoint = File.ReadAllText(Path.Combine(
                endpointDirectory,
                $"Bulk{feature}WorkItemsEndpoint.cs"));
            Assert.Contains($"Bulk{feature}WorkItemsHandler handler", endpoint, StringComparison.Ordinal);
            Assert.Contains($"new Bulk{feature}WorkItemsCommand", endpoint, StringComparison.Ordinal);
            Assert.DoesNotContain("WorkItemService service", endpoint, StringComparison.Ordinal);
        }

        var processor = File.ReadAllText(Path.Combine(bulkDirectory, "WorkItemBulkJobProcessor.cs"));
        Assert.Contains("CreateWorkItemHandler createWorkItemHandler", processor, StringComparison.Ordinal);
        Assert.Contains("MoveWorkItemHandler moveWorkItemHandler", processor, StringComparison.Ordinal);
        Assert.Contains("AssignWorkItemHandler assignWorkItemHandler", processor, StringComparison.Ordinal);
        Assert.Contains("ArchiveWorkItemHandler archiveWorkItemHandler", processor, StringComparison.Ordinal);
        Assert.Contains("createWorkItemHandler.HandleAsync", processor, StringComparison.Ordinal);
        Assert.Contains("moveWorkItemHandler.HandleAsync", processor, StringComparison.Ordinal);
        Assert.Contains("assignWorkItemHandler.HandleAsync", processor, StringComparison.Ordinal);
        Assert.Contains("archiveWorkItemHandler.HandleAsync", processor, StringComparison.Ordinal);
        foreach (var exportModel in new[] { "WorkItemExportResultRow.cs", "WorkItemExportCustomFieldValue.cs" })
        {
            var exportModelSource = File.ReadAllText(Path.Combine(bulkDirectory, exportModel));
            Assert.DoesNotContain("Document", exportModelSource, StringComparison.Ordinal);
            Assert.Contains(
                "namespace Zumbo.Modules.WorkItems.Application.Features.BulkOperations;",
                exportModelSource,
                StringComparison.Ordinal);
        }

        var customFieldExportModel = File.ReadAllText(Path.Combine(
            bulkDirectory,
            "WorkItemExportCustomFieldValue.cs"));
        foreach (var field in new[]
                 {
                     "string FieldKey",
                     "string Type",
                     "string? TextValue",
                     "decimal? NumberValue",
                     "bool? BooleanValue",
                     "DateTimeOffset? DateValueUtc",
                     "string? OptionKey",
                     "bool Indexed",
                     "string SearchValue"
                 })
        {
            Assert.Contains(field, customFieldExportModel, StringComparison.Ordinal);
        }

        var exportRowModel = File.ReadAllText(Path.Combine(bulkDirectory, "WorkItemExportResultRow.cs"));
        Assert.Contains(
            "IReadOnlyCollection<WorkItemExportCustomFieldValue> CustomFields",
            exportRowModel,
            StringComparison.Ordinal);
        Assert.Contains("Array.Empty<WorkItemExportResultRow>()", processor, StringComparison.Ordinal);
        Assert.Contains("new List<WorkItemExportResultRow>", processor, StringComparison.Ordinal);
        Assert.Contains("page.Items.Select(ToExportResultRow)", processor, StringComparison.Ordinal);
        Assert.Contains("private static WorkItemExportRow ToExportRow(WorkItemDocument x)", processor, StringComparison.Ordinal);
        Assert.Contains("private static WorkItemExportResultRow ToExportResultRow(WorkItemDocument x)", processor, StringComparison.Ordinal);

        var moduleComposition = File.ReadAllText(Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Composition",
            "Modules",
            "WorkItems",
            "WorkItemModuleComposition.cs"));
        Assert.Contains("services.AddWorkItemBulkOperations();", moduleComposition, StringComparison.Ordinal);

        var bulkComposition = File.ReadAllText(Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Composition",
            "Modules",
            "WorkItems",
            "WorkItemBulkOperationComposition.cs"));
        Assert.Contains("AddScoped<WorkItemBulkJobProcessor>(provider =>", bulkComposition, StringComparison.Ordinal);
        Assert.DoesNotContain("AddScoped<WorkItemBulkJobProcessor>();", bulkComposition, StringComparison.Ordinal);
        Assert.Contains("provider.GetRequiredService<CreateWorkItemHandler>()", bulkComposition, StringComparison.Ordinal);
        Assert.Contains("provider.GetRequiredService<MoveWorkItemHandler>()", bulkComposition, StringComparison.Ordinal);
        Assert.Contains("provider.GetRequiredService<AssignWorkItemHandler>()", bulkComposition, StringComparison.Ordinal);
        Assert.Contains("provider.GetRequiredService<ArchiveWorkItemHandler>()", bulkComposition, StringComparison.Ordinal);
    }

    [Fact]
    public void AutomationWorkItemActions_UseFeatureHandlersWithCompatibilityFallback()
    {
        var executor = File.ReadAllText(Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Infrastructure",
            "Adapters",
            "WorkItems",
            "WorkItemsCore",
            "AutomationWorkItemActionExecutor.cs"));
        foreach (var handler in new[]
                 {
                     "GetWorkItemHandler",
                     "AssignWorkItemHandler",
                     "ClearAssigneeHandler",
                     "AddLabelHandler",
                     "RemoveLabelHandler",
                     "UpdateWorkItemHandler",
                     "AddCommentHandler"
                 })
        {
            Assert.Contains(handler, executor, StringComparison.Ordinal);
        }

        Assert.Contains("WorkItemService workItems", executor, StringComparison.Ordinal);
        Assert.Contains("getWorkItemHandler.HandleAsync", executor, StringComparison.Ordinal);
        Assert.Contains("assignWorkItemHandler.HandleAsync", executor, StringComparison.Ordinal);
        Assert.Contains("clearAssigneeHandler.HandleAsync", executor, StringComparison.Ordinal);
        Assert.Contains("addLabelHandler.HandleAsync", executor, StringComparison.Ordinal);
        Assert.Contains("removeLabelHandler.HandleAsync", executor, StringComparison.Ordinal);
        Assert.Contains("updateWorkItemHandler.HandleAsync", executor, StringComparison.Ordinal);
        Assert.Contains("addCommentHandler.HandleAsync", executor, StringComparison.Ordinal);

        var composition = File.ReadAllText(Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Composition",
            "Modules",
            "WorkItems",
            "WorkItemAutomationComposition.cs"));
        Assert.Contains(
            "AddScoped<IAutomationActionExecutor>(provider => new AutomationWorkItemActionExecutor(",
            composition,
            StringComparison.Ordinal);

        var endpointHost = File.ReadAllText(EndpointHostPath("AutomationEndpoints.cs"));
        Assert.Contains("services.AddWorkItemAutomationActionAdapter();", endpointHost, StringComparison.Ordinal);
        Assert.DoesNotContain("using Zumbo.Modules.WorkItems;", endpointHost, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "AddScoped<IAutomationActionExecutor, AutomationWorkItemActionExecutor>();",
            composition,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "provider.GetRequiredService<WorkItemService>()",
            composition,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DashboardRenderer_UsesReportHandlersWithCompatibilityFallback()
    {
        var renderer = File.ReadAllText(Path.Combine(
            SourceDirectory,
            "Zumbo.Modules.WorkItems",
            "Application",
            "Features",
            "Dashboards",
            "DashboardRenderer.cs"));
        foreach (var handler in new[]
                 {
                     "ProjectSummaryHandler",
                     "StatusDistributionHandler",
                     "UserWorkloadHandler",
                     "DueDateRisksHandler",
                     "FlowTimeHandler",
                     "CompletionRateHandler",
                     "TeamPerformanceHandler"
                 })
        {
            Assert.Contains(handler, renderer, StringComparison.Ordinal);
        }

        Assert.Contains("WorkItemService reports", renderer, StringComparison.Ordinal);
        Assert.Equal(7, renderer.Split("Handler.HandleAsync", StringSplitOptions.None).Length - 1);

        var composition = File.ReadAllText(Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Composition",
            "Modules",
            "WorkItems",
            "WorkItemReportComposition.cs"));
        Assert.Contains("AddScoped<DashboardRenderer>(provider => new DashboardRenderer(", composition, StringComparison.Ordinal);
        Assert.DoesNotContain("provider.GetRequiredService<WorkItemService>()", composition, StringComparison.Ordinal);

        var endpointHost = File.ReadAllText(EndpointHostPath("DashboardEndpoints.cs"));
        Assert.Contains("services.AddWorkItemDashboardRenderer();", endpointHost, StringComparison.Ordinal);
        Assert.DoesNotContain("services.AddScoped<DashboardRenderer>();", endpointHost, StringComparison.Ordinal);
    }

    [Fact]
    public void RecurrencePublication_UsesFeatureOwnedMapperWithCompatibilityDelegation()
    {
        var moduleDirectory = Path.Combine(SourceDirectory, "Zumbo.Modules.WorkItems");
        var generator = File.ReadAllText(Path.Combine(
            moduleDirectory,
            "Application",
            "Features",
            "Recurrences",
            "RecurringWorkItemGenerator.cs"));
        Assert.Contains("WorkItemPublicationMapper.ToSearchRecord", generator, StringComparison.Ordinal);
        Assert.Contains("WorkItemPublicationMapper.ToRealtimeItem", generator, StringComparison.Ordinal);
        Assert.DoesNotContain("WorkItemService.ToSearchRecord", generator, StringComparison.Ordinal);
        Assert.DoesNotContain("WorkItemService.ToRealtimeItem", generator, StringComparison.Ordinal);

        var searchCompatibility = File.ReadAllText(Path.Combine(
            moduleDirectory,
            "Application",
            "Compatibility",
            "WorkItemService",
            "WorkItemService.SearchFallback.cs"));
        Assert.Contains(
            "WorkItemPublicationMapper.ToSearchRecord(item, organizationId)",
            searchCompatibility,
            StringComparison.Ordinal);

        var realtimeCompatibility = File.ReadAllText(Path.Combine(
            moduleDirectory,
            "Application",
            "Compatibility",
            "WorkItemService",
            "WorkItemService.Mapping.cs"));
        Assert.Contains(
            "WorkItemPublicationMapper.ToRealtimeItem(item)",
            realtimeCompatibility,
            StringComparison.Ordinal);
    }

    [Fact]
    public void NotificationCorePreferencesDeliveryAndCreation_ArePortFocusedVerticalSlicesWithCompatibilityFacades()
    {
        var moduleDirectory = Path.Combine(SourceDirectory, "Zumbo.Modules.Notifications");
        var listDirectory = Path.Combine(moduleDirectory, "Application", "Features", "NotificationsCore");
        var markReadDirectory = Path.Combine(moduleDirectory, "Application", "Features", "ReadState");
        var preferencesDirectory = Path.Combine(moduleDirectory, "Application", "Features", "Preferences");
        var deliveryDirectory = Path.Combine(moduleDirectory, "Application", "Features", "Delivery");
        var creationDirectory = Path.Combine(moduleDirectory, "Application", "Features", "Creation");
        var representativeDirectory = Path.Combine(
            moduleDirectory,
            "Application",
            "Features",
            "RepresentativeNotificationSlices");

        Assert.True(File.Exists(Path.Combine(listDirectory, "ListNotificationsQuery.cs")));
        Assert.True(File.Exists(Path.Combine(listDirectory, "ListNotificationsValidator.cs")));
        Assert.True(File.Exists(Path.Combine(listDirectory, "ListNotificationsHandler.cs")));
        Assert.True(File.Exists(Path.Combine(listDirectory, "ListNotificationsSlice.cs")));
        Assert.True(File.Exists(Path.Combine(markReadDirectory, "MarkNotificationAsReadCommand.cs")));
        Assert.True(File.Exists(Path.Combine(markReadDirectory, "MarkNotificationAsReadValidator.cs")));
        Assert.True(File.Exists(Path.Combine(markReadDirectory, "MarkNotificationAsReadHandler.cs")));
        Assert.True(File.Exists(Path.Combine(markReadDirectory, "MarkNotificationAsReadSlice.cs")));
        foreach (var file in new[]
        {
            "GetNotificationPreferencesQuery.cs",
            "GetNotificationPreferencesHandler.cs",
            "GetNotificationPreferencesSlice.cs",
            "UpdateNotificationPreferencesCommand.cs",
            "UpdateNotificationPreferencesHandler.cs",
            "UpdateNotificationPreferencesSlice.cs",
            "NotificationPreferenceAccess.cs",
            "NotificationPreferenceValidation.cs",
            "NotificationPreferenceMapper.cs"
        })
        {
            Assert.True(File.Exists(Path.Combine(preferencesDirectory, file)), $"Missing preference file: {file}");
        }
        foreach (var file in new[]
        {
            "GetNotificationDeliveryMetricsQuery.cs",
            "GetNotificationDeliveryMetricsHandler.cs",
            "GetNotificationDeliveryMetricsSlice.cs",
            "ListNotificationDeadLettersQuery.cs",
            "ListNotificationDeadLettersHandler.cs",
            "ListNotificationDeadLettersSlice.cs",
            "ReplayNotificationDeadLetterCommand.cs",
            "ReplayNotificationDeadLetterHandler.cs",
            "ReplayNotificationDeadLetterSlice.cs",
            "NotificationDeliveryPolicy.cs",
            "DispatchNotificationEmailsCommand.cs",
            "DispatchNotificationEmailsHandler.cs",
            "DispatchNotificationEmailsSlice.cs",
            "NotificationEmailRetryPolicy.cs"
        })
        {
            Assert.True(File.Exists(Path.Combine(deliveryDirectory, file)), $"Missing delivery file: {file}");
        }
        foreach (var file in new[]
        {
            "CreateNotificationCommand.cs",
            "CreateNotificationHandler.cs",
            "CreateNotificationSlice.cs",
            "NotificationCreationPolicy.cs",
            "NotificationCreationLockAccess.cs",
            "NotificationDigestSchedule.cs"
        })
        {
            Assert.True(File.Exists(Path.Combine(creationDirectory, file)), $"Missing creation file: {file}");
        }
        Assert.Empty(
            Directory.Exists(representativeDirectory)
                ? Directory.GetFiles(representativeDirectory, "*.cs", SearchOption.AllDirectories)
                : []);

        var listSlice = File.ReadAllText(Path.Combine(listDirectory, "ListNotificationsSlice.cs"));
        var markReadSlice = File.ReadAllText(Path.Combine(markReadDirectory, "MarkNotificationAsReadSlice.cs"));
        Assert.DoesNotContain("NotificationService", listSlice, StringComparison.Ordinal);
        Assert.DoesNotContain("NotificationService", markReadSlice, StringComparison.Ordinal);
        Assert.Contains("IDocumentRepository<NotificationDocument>", listSlice, StringComparison.Ordinal);
        Assert.Contains("ICurrentUser", listSlice, StringComparison.Ordinal);
        Assert.Contains("IDocumentRepository<NotificationDocument>", markReadSlice, StringComparison.Ordinal);
        Assert.Contains("ICurrentUser", markReadSlice, StringComparison.Ordinal);

        var listFacade = File.ReadAllText(Path.Combine(
            moduleDirectory,
            "Application",
            "Compatibility",
            "NotificationService",
            "NotificationService.Inbox.cs"));
        Assert.Contains("listNotificationsHandler.HandleAsync", listFacade, StringComparison.Ordinal);
        Assert.Contains("markNotificationAsReadHandler.HandleAsync", listFacade, StringComparison.Ordinal);
        var creationSlice = File.ReadAllText(Path.Combine(creationDirectory, "CreateNotificationSlice.cs"));
        var creationFacade = File.ReadAllText(Path.Combine(
            moduleDirectory,
            "Application",
            "Compatibility",
            "NotificationService",
            "NotificationService.Creation.cs"));
        Assert.DoesNotContain("NotificationService", creationSlice, StringComparison.Ordinal);
        Assert.Contains("CreateNotificationHandler", creationFacade, StringComparison.Ordinal);
        Assert.Contains("CreateNotificationCommand", creationFacade, StringComparison.Ordinal);
        var dispatchFacade = File.ReadAllText(Path.Combine(
            moduleDirectory,
            "Application",
            "Compatibility",
            "NotificationService",
            "NotificationService.Delivery.cs"));
        Assert.Contains("DispatchNotificationEmailsHandler", dispatchFacade, StringComparison.Ordinal);
        Assert.Contains("DispatchNotificationEmailsCommand", dispatchFacade, StringComparison.Ordinal);

        var dispatcherHost = File.ReadAllText(Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Infrastructure",
            "BackgroundServices",
            "Delivery",
            "NotificationEmailDispatcherHostedService.cs"));
        Assert.Contains("GetRequiredService<DispatchNotificationEmailsHandler>", dispatcherHost, StringComparison.Ordinal);
        Assert.DoesNotContain("GetRequiredService<NotificationService>", dispatcherHost, StringComparison.Ordinal);

        var facadeFiles = Directory.GetFiles(
            Path.Combine(moduleDirectory, "Application", "Compatibility", "NotificationService"),
            "*.cs",
            SearchOption.TopDirectoryOnly);
        Assert.Equal(4, facadeFiles.Length);
        Assert.Equal(
            [
                "NotificationService.Creation.cs",
                "NotificationService.Delivery.cs",
                "NotificationService.Inbox.cs",
                "NotificationService.Preferences.cs"
            ],
            facadeFiles
                .Select(path => Path.GetFileName(path)!)
                .Order(StringComparer.Ordinal)
                .ToArray());

        var endpointHost = File.ReadAllText(EndpointHostPath("NotificationEndpoints.cs"));
        Assert.Contains("services.AddNotificationServices(configuration)", endpointHost, StringComparison.Ordinal);
        Assert.DoesNotContain("IDocumentRepository", endpointHost, StringComparison.Ordinal);
        Assert.DoesNotContain("NotificationDocument", endpointHost, StringComparison.Ordinal);
        Assert.Contains("GetNotificationPreferencesHandler handler", endpointHost, StringComparison.Ordinal);
        Assert.Contains("UpdateNotificationPreferencesHandler handler", endpointHost, StringComparison.Ordinal);
        Assert.DoesNotContain("service.GetPreferencesAsync(", endpointHost, StringComparison.Ordinal);
        Assert.DoesNotContain("service.UpdatePreferencesAsync(", endpointHost, StringComparison.Ordinal);
        Assert.Contains("GetNotificationDeliveryMetricsHandler handler", endpointHost, StringComparison.Ordinal);
        Assert.Contains("ListNotificationDeadLettersHandler handler", endpointHost, StringComparison.Ordinal);
        Assert.Contains("ReplayNotificationDeadLetterHandler handler", endpointHost, StringComparison.Ordinal);
        Assert.DoesNotContain("service.GetDeliveryMetricsAsync(", endpointHost, StringComparison.Ordinal);
        Assert.DoesNotContain("service.ListDeadLettersAsync(", endpointHost, StringComparison.Ordinal);
        Assert.DoesNotContain("service.ReplayDeadLetterAsync(", endpointHost, StringComparison.Ordinal);
        Assert.Contains("NotificationDeliveryReplayed", endpointHost, StringComparison.Ordinal);

        var composition = File.ReadAllText(Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Composition",
            "Modules",
            "Notifications",
            "NotificationModuleComposition.cs"));
        Assert.Contains("AddScoped<ListNotificationsHandler>(provider =>", composition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<MarkNotificationAsReadHandler>(provider =>", composition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<GetNotificationPreferencesHandler>(provider =>", composition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<UpdateNotificationPreferencesHandler>(provider =>", composition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<GetNotificationDeliveryMetricsHandler>(provider =>", composition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<ListNotificationDeadLettersHandler>(provider =>", composition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<ReplayNotificationDeadLetterHandler>(provider =>", composition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<CreateNotificationHandler>(provider =>", composition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<DispatchNotificationEmailsHandler>(provider =>", composition, StringComparison.Ordinal);
    }

    [Fact]
    public void AuditWriteAndQuery_ArePortFocusedVerticalSlicesWithCompatibilityFacades()
    {
        var moduleDirectory = Path.Combine(SourceDirectory, "Zumbo.Modules.Audit");
        var writeDirectory = Path.Combine(moduleDirectory, "Application", "Features", "WriteAuditLog");
        var queryDirectory = Path.Combine(moduleDirectory, "Application", "Features", "QueryAuditLog");
        var representativeDirectory = Path.Combine(
            moduleDirectory,
            "Application",
            "Features",
            "RepresentativeAuditSlices");

        Assert.True(File.Exists(Path.Combine(writeDirectory, "WriteAuditLogCommand.cs")));
        Assert.True(File.Exists(Path.Combine(writeDirectory, "WriteAuditLogValidator.cs")));
        Assert.True(File.Exists(Path.Combine(writeDirectory, "WriteAuditLogHandler.cs")));
        Assert.True(File.Exists(Path.Combine(writeDirectory, "WriteAuditLogSlice.cs")));
        Assert.True(File.Exists(Path.Combine(queryDirectory, "AuditLogQuery.cs")));
        Assert.True(File.Exists(Path.Combine(queryDirectory, "QueryAuditLogValidator.cs")));
        Assert.True(File.Exists(Path.Combine(queryDirectory, "QueryAuditLogHandler.cs")));
        Assert.True(File.Exists(Path.Combine(queryDirectory, "QueryAuditLogSlice.cs")));
        Assert.Empty(
            Directory.Exists(representativeDirectory)
                ? Directory.GetFiles(representativeDirectory, "*.cs", SearchOption.AllDirectories)
                : []);

        var writeSlice = File.ReadAllText(Path.Combine(writeDirectory, "WriteAuditLogSlice.cs"));
        var querySlice = File.ReadAllText(Path.Combine(queryDirectory, "QueryAuditLogSlice.cs"));
        Assert.DoesNotContain("AuditService", writeSlice, StringComparison.Ordinal);
        Assert.DoesNotContain("AuditService", querySlice, StringComparison.Ordinal);
        Assert.Contains("IDocumentRepository<AuditLogDocument>", writeSlice, StringComparison.Ordinal);
        Assert.Contains("IAuditTenantResolver", writeSlice, StringComparison.Ordinal);
        Assert.Contains("IAuditRequestContext", writeSlice, StringComparison.Ordinal);
        Assert.Contains("IDistributedLockProvider", writeSlice, StringComparison.Ordinal);
        Assert.Contains("IDocumentRepository<AuditLogDocument>", querySlice, StringComparison.Ordinal);
        Assert.Contains("IAuditAccessChecker", querySlice, StringComparison.Ordinal);

        var facade = File.ReadAllText(Path.Combine(
            moduleDirectory,
            "Application",
            "Compatibility",
            "AuditService.cs"));
        Assert.Contains("writeAuditLogHandler.HandleUncheckedAsync", facade, StringComparison.Ordinal);
        Assert.Contains("queryAuditLogHandler.HandleAsync", facade, StringComparison.Ordinal);

        var endpointHost = File.ReadAllText(EndpointHostPath("AuditEndpoints.cs"));
        Assert.Contains("services.AddAuditServices()", endpointHost, StringComparison.Ordinal);
        Assert.DoesNotContain("IDocumentRepository", endpointHost, StringComparison.Ordinal);
        Assert.DoesNotContain("AuditLogDocument", endpointHost, StringComparison.Ordinal);

        var composition = File.ReadAllText(Path.Combine(
            SourceDirectory,
            "Zumbo.Api",
            "Composition",
            "Modules",
            "Audit",
            "AuditModuleComposition.cs"));
        Assert.Contains("AddScoped<WriteAuditLogHandler>(provider =>", composition, StringComparison.Ordinal);
        Assert.Contains("AddScoped<QueryAuditLogHandler>(provider =>", composition, StringComparison.Ordinal);
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
    public void EndpointRepositoryDependencies_AreRestrictedToCompositionMethods()
    {
        var endpointDirectory = Path.Combine(SourceDirectory, "Zumbo.Api", "Presentation", "Endpoints");
        var violations = Directory.GetFiles(endpointDirectory, "*.cs", SearchOption.AllDirectories)
            .SelectMany(path =>
            {
                var root = CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetCompilationUnitRoot();
                return root.DescendantNodes()
                    .OfType<GenericNameSyntax>()
                    .Where(type => type.Identifier.ValueText == "IDocumentRepository")
                    .Select(type => new
                    {
                        Path = Path.GetRelativePath(endpointDirectory, path),
                        Method = type.FirstAncestorOrSelf<MethodDeclarationSyntax>()?.Identifier.ValueText
                    });
            })
            .Where(reference => reference.Method is null
                || !reference.Method.StartsWith("Add", StringComparison.Ordinal))
            .OrderBy(reference => reference.Path, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Endpoint mapping methods must use application handlers/services rather than repositories:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, violations.Select(
                    violation => $"{violation.Path}: {violation.Method ?? "type scope"}")));
    }

    [Fact]
    public void WorkItemGraphTraversal_UsesBoundedIndexedRepositoryOperations()
    {
        var path = Path.Combine(
            SourceDirectory,
            "Zumbo.Modules.WorkItems",
            "Application",
            "Features",
            "WorkItemsCore",
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
        var notificationHost = File.ReadAllText(EndpointHostPath("NotificationEndpoints.cs"));
        var gateway = ReadSourceScope(Path.Combine(SourceDirectory, "Zumbo.Gateway", "GatewayHost"));
        Assert.DoesNotContain("Zumbo.Modules.WorkItems", notificationAdapters, StringComparison.Ordinal);
        var notificationComposition = ReadSourceScope(
            Path.Combine(SourceDirectory, "Zumbo.Api", "Composition", "Modules", "Notifications"));
        var workItemBackgroundComposition = ReadSourceScope(
            Path.Combine(SourceDirectory, "Zumbo.Api", "Composition", "Modules", "WorkItems"));
        Assert.DoesNotContain("DueDateReminderHostedService", notificationHost, StringComparison.Ordinal);
        Assert.DoesNotContain("DueDateReminderHostedService", notificationComposition, StringComparison.Ordinal);
        Assert.Contains("AddWorkItemBackgroundServices(configuration)", workItemBackgroundComposition, StringComparison.Ordinal);
        Assert.Contains("AddHostedService<DueDateReminderHostedService>()", workItemBackgroundComposition, StringComparison.Ordinal);
        Assert.Contains("AddHostedService<WorkItemRecurrenceSchedulerHostedService>()", workItemBackgroundComposition, StringComparison.Ordinal);
        Assert.Contains("AddHostedService<WebhookDispatcherHostedService>()", workItemBackgroundComposition, StringComparison.Ordinal);
        Assert.Contains("AddHostedService<DevelopmentWebhookReceiptRetentionHostedService>()", workItemBackgroundComposition, StringComparison.Ordinal);
        Assert.Contains("/api/notifications/{**catch-all}", gateway, StringComparison.Ordinal);
        Assert.Contains("NotificationExtractionEnabled", gateway, StringComparison.Ordinal);
        Assert.Contains("Order = -100", gateway, StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> ModuleProjectFiles() =>
        Directory.GetDirectories(SourceDirectory, "Zumbo.Modules.*")
            .SelectMany(directory => Directory.GetFiles(directory, "*.csproj"))
            .Order(StringComparer.Ordinal)
            .ToList();

    private static string EndpointHostPath(string fileName)
    {
        var endpointDirectory = Path.Combine(SourceDirectory, "Zumbo.Api", "Presentation", "Endpoints");
        return Assert.Single(Directory.GetFiles(endpointDirectory, fileName, SearchOption.AllDirectories));
    }

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

    private static string[] FileNames(string directory) =>
        Directory.GetFiles(directory, "*.cs", SearchOption.TopDirectoryOnly)
            .Select(path => Path.GetFileName(path)!)
            .Order(StringComparer.Ordinal)
            .ToArray();

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
