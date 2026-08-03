using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Zumbo.ArchitectureTests.RefactorValidation;

public sealed class RefactorRuntimeContractTests
{
    private static readonly HashSet<string> HttpMapMethods =
        ["MapGet", "MapPost", "MapPut", "MapPatch", "MapDelete", "MapMethods"];

    private static readonly HashSet<string> DiMethods =
    [
        "AddScoped", "AddSingleton", "AddTransient",
        "TryAddScoped", "TryAddSingleton", "TryAddTransient",
        "AddKeyedScoped", "AddKeyedSingleton", "AddKeyedTransient",
        "AddHostedService", "Configure", "PostConfigure", "AddOptions", "AddHttpClient"
    ];

    private static readonly string[] ReplacedVerticalSliceDiRegistrations =
    [
        "services.AddScoped<RegisterUserHandler>();",
        "services.AddScoped<SearchUsersHandler>();",
        "services.AddScoped<CreateOrganizationHandler>();",
        "services.AddScoped<ListOrganizationsHandler>();",
        "services.AddScoped<CreateTeamHandler>();",
        "services.AddScoped<ListTeamsHandler>();",
        "services.AddScoped<CreateProjectHandler>();",
        "services.AddScoped<ListProjectsHandler>();",
        "services.AddScoped<CreateBoardHandler>();",
        "services.AddScoped<ListBoardsByProjectHandler>();",
        "services.AddScoped<UpsertWorkflowHandler>();",
        "services.AddScoped<GetWorkflowHandler>();",
        "services.AddScoped<CreateWorkItemHandler>();",
        "services.AddScoped<SearchWorkItemsHandler>();",
        "services.AddScoped<ListNotificationsHandler>();",
        "services.AddScoped<MarkNotificationAsReadHandler>();"
    ];

    private static readonly string[] PortFocusedVerticalSliceDiRegistrations =
    [
        "services.AddScoped<RegisterUserHandler>(provider=>newRegisterUserHandler("
        + "provider.GetRequiredService<IUserRepository>(),"
        + "provider.GetRequiredService<IRefreshSessionStore>(),"
        + "provider.GetRequiredService<IDurableTransactionRunner>(),"
        + "provider.GetRequiredService<IPasswordHasher>(),"
        + "provider.GetRequiredService<ITokenIssuer>(),"
        + "provider.GetRequiredService<IOptions<JwtOptions>>(),"
        + "provider.GetRequiredService<IOptions<IdentityBootstrapOptions>>(),"
        + "provider.GetRequiredService<IDistributedLockProvider>(),"
        + "provider.GetRequiredService<IOptions<DistributedLockOptions>>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<IRegistrationProvisioningPolicy>(),"
        + "provider.GetRequiredService<ISessionClientContext>()));",
        "services.AddScoped<SearchUsersHandler>(provider=>newSearchUsersHandler("
        + "provider.GetRequiredService<IUserRepository>(),"
        + "provider.GetRequiredService<ICurrentUser>()));",
        "services.AddScoped<CreateOrganizationHandler>(provider=>newCreateOrganizationHandler("
        + "provider.GetRequiredService<IDocumentRepository<OrganizationDocument>>(),"
        + "provider.GetRequiredService<IDistributedLockProvider>(),"
        + "provider.GetRequiredService<IOptions<DistributedLockOptions>>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IOrganizationAuditWriter>()));",
        "services.AddScoped<ListOrganizationsHandler>(provider=>newListOrganizationsHandler("
        + "provider.GetRequiredService<IDocumentRepository<OrganizationDocument>>(),"
        + "provider.GetRequiredService<ICurrentUser>()));",
        "services.AddScoped<CreateTeamHandler>(provider=>newCreateTeamHandler("
        + "provider.GetRequiredService<IDocumentRepository<TeamDocument>>(),"
        + "provider.GetRequiredService<ITeamUserDirectory>(),"
        + "provider.GetRequiredService<ITeamOrganizationDirectory>(),"
        + "provider.GetRequiredService<ITeamAuditWriter>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<ICurrentUser>()));",
        "services.AddScoped<ListTeamsHandler>(provider=>newListTeamsHandler("
        + "provider.GetRequiredService<IDocumentRepository<TeamDocument>>(),"
        + "provider.GetRequiredService<ITeamOrganizationDirectory>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<ICurrentUser>()));",
        "services.AddScoped<CreateProjectHandler>(provider=>newCreateProjectHandler("
        + "provider.GetRequiredService<IDocumentRepository<ProjectDocument>>(),"
        + "provider.GetRequiredService<IProjectMemberDirectory>(),"
        + "provider.GetRequiredService<IProjectOrganizationDirectory>(),"
        + "provider.GetRequiredService<IProjectAuditWriter>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<ICurrentUser>()));",
        "services.AddScoped<ListProjectsHandler>(provider=>newListProjectsHandler("
        + "provider.GetRequiredService<IDocumentRepository<ProjectDocument>>(),"
        + "provider.GetRequiredService<IProjectOrganizationDirectory>(),"
        + "provider.GetRequiredService<ICurrentUser>()));",
        "services.AddScoped<CreateBoardHandler>(provider=>newCreateBoardHandler("
        + "provider.GetRequiredService<IDocumentRepository<BoardDocument>>(),"
        + "provider.GetRequiredService<IBoardProjectAccessChecker>(),"
        + "provider.GetRequiredService<IDistributedLockProvider>(),"
        + "provider.GetRequiredService<IOptions<DistributedLockOptions>>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IBoardAuditWriter>()));",
        "services.AddScoped<ListBoardsByProjectHandler>(provider=>newListBoardsByProjectHandler("
        + "provider.GetRequiredService<IDocumentRepository<BoardDocument>>(),"
        + "provider.GetRequiredService<IBoardProjectAccessChecker>(),"
        + "provider.GetRequiredService<ICurrentUser>()));",
        "services.AddScoped<UpsertWorkflowHandler>(provider=>newUpsertWorkflowHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkflowDefinitionDocument>>(),"
        + "provider.GetRequiredService<IWorkflowProjectAccessChecker>(),"
        + "provider.GetRequiredService<IDistributedLockProvider>(),"
        + "provider.GetRequiredService<IOptions<DistributedLockOptions>>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<IWorkflowAuditWriter>(),"
        + "provider.GetRequiredService<IExpectedVersionAccessor>(),"
        + "provider.GetRequiredService<IWorkflowPublicationGuard>()));",
        "services.AddScoped<GetWorkflowHandler>(provider=>newGetWorkflowHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkflowDefinitionDocument>>(),"
        + "provider.GetRequiredService<IWorkflowProjectAccessChecker>(),"
        + "provider.GetRequiredService<IDistributedLockProvider>(),"
        + "provider.GetRequiredService<IOptions<DistributedLockOptions>>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<IExpectedVersionAccessor>()));",
        "services.AddScoped<CreateWorkItemHandler>(provider=>newCreateWorkItemHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),"
        + "provider.GetRequiredService<IWorkItemNotificationPublisher>(),"
        + "provider.GetRequiredService<IWorkItemAuditPublisher>(),"
        + "provider.GetRequiredService<IClock>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<IWorkItemTeamPolicy>(),"
        + "provider.GetRequiredService<IBoardPlacementPolicy>(),"
        + "provider.GetRequiredService<IDistributedLockProvider>(),"
        + "provider.GetRequiredService<IOptions<DistributedLockOptions>>(),"
        + "provider.GetRequiredService<IWorkItemSearchPublisher>(),"
        + "provider.GetRequiredService<IWorkItemRealtimePublisher>(),"
        + "provider.GetRequiredService<IWorkItemCacheInvalidationPublisher>(),"
        + "provider.GetRequiredService<IWorkItemActivityStore>(),"
        + "provider.GetRequiredService<WorkItemGraphService>(),"
        + "provider.GetService<WorkItemWipProjection>(),"
        + "provider.GetRequiredService<WorkItemRankService>(),"
        + "provider.GetRequiredService<IWorkItemTypeSchemaPolicy>(),"
        + "provider.GetService<WorkItemCollaborationService>(),"
        + "provider.GetService<IWorkItemAutomationEventPublisher>(),"
        + "provider.GetService<IWorkItemAutomationChainContextAccessor>()));",
        "services.AddScoped<SearchWorkItemsHandler>(provider=>newSearchWorkItemsHandler("
        + "provider.GetRequiredService<IDocumentRepository<WorkItemDocument>>(),"
        + "provider.GetRequiredService<ICurrentUser>(),"
        + "provider.GetRequiredService<IProjectPermissionChecker>(),"
        + "provider.GetRequiredService<IWorkItemTypeSchemaPolicy>(),"
        + "provider.GetRequiredService<IWorkItemSearchIndex>(),"
        + "provider.GetRequiredService<IWorkItemActivityStore>(),"
        + "provider.GetRequiredService<IOptions<SearchOptions>>()));",
        "services.AddScoped<ListNotificationsHandler>(provider=>newListNotificationsHandler("
        + "provider.GetRequiredService<IDocumentRepository<NotificationDocument>>(),"
        + "provider.GetRequiredService<ICurrentUser>()));",
        "services.AddScoped<MarkNotificationAsReadHandler>(provider=>newMarkNotificationAsReadHandler("
        + "provider.GetRequiredService<IDocumentRepository<NotificationDocument>>(),"
        + "provider.GetRequiredService<ICurrentUser>()));"
    ];

    private static string ProjectDirectory => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

    private static string RepositoryDirectory => Directory.GetParent(ProjectDirectory)!.FullName;

    [Fact]
    public void RefactorSnapshot_PreservesRuntimeContractsAndSeparatesIntentionalTimeoutChanges()
    {
        var baselineFiles = RefactorSourceReader.ReadGit(
            RepositoryDirectory,
            RefactorSemanticInventory.BaselineCommit);
        var targetFiles = RefactorSourceReader.ReadWorkingTree(ProjectDirectory);

        var endpoints = EndpointContracts(baselineFiles);
        var targetEndpoints = EndpointContracts(targetFiles);
        var registrations = DiContracts(baselineFiles);
        var targetRegistrations = DiContracts(targetFiles);
        var migrations = MigrationContracts(baselineFiles);
        var targetMigrations = MigrationContracts(targetFiles);
        var mongo = MongoContracts(baselineFiles);
        var targetMongo = MongoContracts(targetFiles);
        var serialization = SerializationContracts(baselineFiles);
        var targetSerialization = SerializationContracts(targetFiles);
        var messaging = MessagingContracts(baselineFiles);
        var targetMessaging = MessagingContracts(targetFiles);

        AssertExact("HTTP endpoint mappings", endpoints, targetEndpoints);
        AssertExactWithAllowedReplacements(
            "DI registrations",
            registrations,
            targetRegistrations,
            ReplacedVerticalSliceDiRegistrations,
            PortFocusedVerticalSliceDiRegistrations);
        AssertExact("PostgreSQL migrations", migrations, targetMigrations);
        AssertExactWithAllowedAdditions(
            "Mongo contracts",
            mongo,
            targetMongo,
            [
                "mongo.GetCollection<BsonDocument>(\"users\",\"Identity\")",
                "mongo.GetCollection<BsonDocument>(target.Collection,target.Module)",
                "mongo.GetCollection<BsonDocument>(target.Collection,target.Module)"
            ]);
        AssertExact("serialization attributes", serialization, targetSerialization);
        AssertExact("messaging contracts", messaging, targetMessaging);
        AssertConfigurationChangesAreIntentionalAndBounded();

        var report = RuntimeReport(
            endpoints.Count,
            registrations.Count,
            migrations.Count,
            mongo.Count,
            serialization.Count,
            messaging.Count);
        var reportPath = Path.Combine(ProjectDirectory, "docs", "architecture", "refactor-runtime-contracts.json");
        if (Environment.GetEnvironmentVariable("ZUMBO_UPDATE_REFACTOR_REPORTS") == "1")
        {
            File.WriteAllText(reportPath, report);
        }
        Assert.True(File.Exists(reportPath), "Missing generated runtime contract report.");
        Assert.Equal(report, File.ReadAllText(reportPath).Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    private static string RuntimeReport(
        int endpoints,
        int registrations,
        int migrations,
        int mongo,
        int serialization,
        int messaging)
    {
        var payload = new
        {
            schemaVersion = 1,
            baselineCommit = RefactorSemanticInventory.BaselineCommit,
            refactorSnapshotCommit = RefactorSemanticInventory.RefactorSnapshotCommit,
            passed = true,
            exactContractCounts = new { endpoints, registrations, migrations, mongo, serialization, messaging },
            missingContracts = Array.Empty<string>(),
            changedContracts = Array.Empty<string>(),
            intentionalRuntimeContractAdditions = new[]
            {
                "Identity compatibility migration reads the users collection to normalize legacy document versions.",
                "Infrastructure marker cleanup resolves its module-owned collections for dry-run counting and idempotent updates."
            },
            intentionalRuntimeContractReplacements = new[]
            {
                "RegisterUserHandler and SearchUsersHandler remain scoped self-services; explicit factories select their port-focused constructors while compatibility constructors remain available.",
                "CreateOrganizationHandler and ListOrganizationsHandler remain scoped self-services; explicit factories select their port-focused constructors while compatibility constructors remain available.",
                "CreateTeamHandler and ListTeamsHandler remain scoped self-services; explicit factories select their port-focused constructors while compatibility constructors remain available.",
                "CreateProjectHandler and ListProjectsHandler remain scoped self-services; explicit factories select their port-focused constructors while compatibility constructors remain available.",
                "CreateBoardHandler and ListBoardsByProjectHandler remain scoped self-services; explicit factories select their port-focused constructors while compatibility constructors remain available.",
                "UpsertWorkflowHandler and GetWorkflowHandler remain scoped self-services; explicit factories select their port-focused constructors while compatibility constructors remain available.",
                "CreateWorkItemHandler and SearchWorkItemsHandler remain scoped self-services; explicit factories select their port-focused constructors while compatibility constructors remain available.",
                "ListNotificationsHandler and MarkNotificationAsReadHandler remain scoped self-services; explicit factories select their port-focused constructors while compatibility constructors remain available."
            },
            intentionalConfigurationChanges = new[]
            {
                "Local Compose access tokens use an overridable 1440-minute demo lifetime; the base default remains 30 minutes.",
                "Local Compose Mongo commands use an overridable 300-second migration window; the base default remains 30 seconds.",
                "API dependency health timeout is configurable with a 5-second base default and a 30-second local Compose override.",
                "Gateway local upstream timeout changed from 30 to 60 seconds.",
                "Mongo, Redis, MinIO, and OpenSearch local health windows were lengthened.",
                "OpenSearch local retries/start period changed from 20/45s to 60/120s."
            }
        };
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true })
            .Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }

    private static IReadOnlyList<string> EndpointContracts(
        IReadOnlyList<RefactorSourceReader.SourceFile> files) =>
        Parsed(files, file => file.Path.StartsWith("Backend/src/Zumbo.Api/Endpoints/", StringComparison.Ordinal))
            .SelectMany(source => source.Root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            .Where(invocation => HttpMapMethods.Contains(InvocationName(invocation)))
            .Select(invocation => Normalize(
                (SyntaxNode?)invocation.FirstAncestorOrSelf<StatementSyntax>() ?? invocation))
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<string> DiContracts(IReadOnlyList<RefactorSourceReader.SourceFile> files) =>
        Parsed(files, _ => true)
            .SelectMany(source => source.Root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            .Where(invocation => DiMethods.Contains(InvocationName(invocation)))
            .Select(invocation => Normalize(
                (SyntaxNode?)invocation.FirstAncestorOrSelf<StatementSyntax>() ?? invocation))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<string> MigrationContracts(
        IReadOnlyList<RefactorSourceReader.SourceFile> files)
    {
        var roots = Parsed(files, file =>
                file.Path.StartsWith("Backend/src/Zumbo.Persistence.PostgreSql/PostgreSqlMigrations", StringComparison.Ordinal))
            .ToArray();
        var migrationInvocations = roots
            .SelectMany(source => source.Root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            .Where(invocation => InvocationName(invocation) == "Create"
                && invocation.Expression.ToString().Contains("Migration", StringComparison.Ordinal))
            .ToArray();
        var referencedSqlNames = migrationInvocations
            .SelectMany(invocation => invocation.ArgumentList.Arguments.Skip(2).Select(argument => argument.Expression.ToString()))
            .ToHashSet(StringComparer.Ordinal);
        var initializers = roots
            .SelectMany(source => source.Root.DescendantNodes().OfType<VariableDeclaratorSyntax>())
            .Where(variable => variable.Initializer is not null
                && referencedSqlNames.Contains(variable.Identifier.ValueText))
            .GroupBy(variable => variable.Identifier.ValueText, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(variable => Normalize(variable.Initializer!.Value))
                    .Distinct(StringComparer.Ordinal)
                    .Single(),
                StringComparer.Ordinal);

        return migrationInvocations
            .Select(invocation =>
            {
                var arguments = invocation.ArgumentList.Arguments;
                Assert.Equal(4, arguments.Count);
                var upName = arguments[2].Expression.ToString();
                var downName = arguments[3].Expression.ToString();
                return string.Join('|',
                    Normalize(arguments[0].Expression),
                    Normalize(arguments[1].Expression),
                    upName,
                    initializers[upName],
                    downName,
                    initializers[downName]);
            })
            .OrderBy(item => long.Parse(item[..item.IndexOf('|')]))
            .ToArray();
    }

    private static IReadOnlyList<string> MongoContracts(
        IReadOnlyList<RefactorSourceReader.SourceFile> files) =>
        Parsed(files, _ => true)
            .SelectMany(source => source.Root.DescendantNodes())
            .Where(node => node switch
            {
                ObjectCreationExpressionSyntax creation =>
                    creation.Type.ToString().Contains("CreateIndexModel", StringComparison.Ordinal),
                InvocationExpressionSyntax invocation => InvocationName(invocation) is
                    "GetCollection" or "CreateOneAsync" or "CreateManyAsync" or "Indexes",
                _ => false
            })
            .Select(Normalize)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<string> SerializationContracts(
        IReadOnlyList<RefactorSourceReader.SourceFile> files) =>
        Parsed(files, _ => true)
            .SelectMany(source => source.Root.DescendantNodes().OfType<AttributeSyntax>())
            .Where(attribute => SerializationAttributeName(attribute) is
                "JsonPropertyName" or "BsonElement" or "BsonDiscriminator" or "BsonKnownTypes")
            .Select(Normalize)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<string> MessagingContracts(
        IReadOnlyList<RefactorSourceReader.SourceFile> files) =>
        Parsed(files, _ => true)
            .SelectMany(source => source.Root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            .Where(type => MessagingType(type.Identifier.ValueText))
            .SelectMany(type => type.Members
                .Where(member => member is not BaseTypeDeclarationSyntax)
                .Select(member => $"{QualifiedTypeName(type)}|{Normalize(member)}"))
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static bool MessagingType(string name) =>
        name.Contains("Event", StringComparison.Ordinal)
        || name.Contains("Message", StringComparison.Ordinal)
        || name.Contains("Inbox", StringComparison.Ordinal)
        || name.Contains("Outbox", StringComparison.Ordinal)
        || name.Contains("DeadLetter", StringComparison.Ordinal);

    private static string QualifiedTypeName(TypeDeclarationSyntax type)
    {
        var namespaceName = string.Join(".", type.Ancestors()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .Reverse()
            .Select(item => item.Name.ToString()));
        return string.IsNullOrEmpty(namespaceName)
            ? type.Identifier.ValueText
            : $"{namespaceName}.{type.Identifier.ValueText}";
    }

    private static void AssertConfigurationChangesAreIntentionalAndBounded()
    {
        var baselineSettings = RefactorSourceReader.ReadGitFile(
            RepositoryDirectory,
            RefactorSemanticInventory.BaselineCommit,
            "Backend/src/Zumbo.Api/appsettings.json");
        var targetSettings = File.ReadAllText(Path.Combine(
            ProjectDirectory,
            "Backend", "src", "Zumbo.Api", "appsettings.json"));
        var baselineLeaves = FlattenJson(baselineSettings);
        var targetLeaves = FlattenJson(targetSettings);

        Assert.All(baselineLeaves, item => Assert.Equal(item.Value, targetLeaves[item.Key]));
        var addedSettings = targetLeaves.Keys.Except(baselineLeaves.Keys, StringComparer.Ordinal).ToArray();
        Assert.Equal(["HealthChecks:DependencyTimeoutSeconds"], addedSettings);
        Assert.Equal("5", targetLeaves["HealthChecks:DependencyTimeoutSeconds"]);

        var baselineCompose = RefactorSourceReader.ReadGitFile(
            RepositoryDirectory,
            RefactorSemanticInventory.BaselineCommit,
            "Backend/docker-compose.yml");
        var targetCompose = File.ReadAllText(Path.Combine(ProjectDirectory, "Backend", "docker-compose.yml"));
        var difference = LineMultisetDifference(baselineCompose, targetCompose);

        Assert.Equal(
        [
            "Gateway__UpstreamTimeoutSeconds: 30",
            "retries: 20",
            "start_period: 45s",
            "timeout: 3s",
            "timeout: 5s",
            "timeout: 5s",
            "timeout: 5s"
        ], difference.Removed);
        Assert.Equal(
        [
            "Gateway__UpstreamTimeoutSeconds: 60",
            "HealthChecks__DependencyTimeoutSeconds: 30",
            "HealthChecks__DependencyTimeoutSeconds: 30",
            "Jwt__AccessTokenMinutes: ${ZUMBO_ACCESS_TOKEN_MINUTES:-1440}",
            "Jwt__AccessTokenMinutes: ${ZUMBO_ACCESS_TOKEN_MINUTES:-1440}",
            "MongoDb__CommandTimeoutSeconds: ${ZUMBO_MONGO_COMMAND_TIMEOUT_SECONDS:-300}",
            "MongoDb__CommandTimeoutSeconds: ${ZUMBO_MONGO_COMMAND_TIMEOUT_SECONDS:-300}",
            "retries: 60",
            "start_period: 120s",
            "timeout: 15s",
            "timeout: 20s",
            "timeout: 20s",
            "timeout: 20s"
        ], difference.Added);
    }

    private static IReadOnlyDictionary<string, string> FlattenJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        AddJsonLeaves(document.RootElement, string.Empty, result);
        return result;
    }

    private static void AddJsonLeaves(
        JsonElement element,
        string path,
        IDictionary<string, string> result)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                AddJsonLeaves(property.Value, path.Length == 0 ? property.Name : $"{path}:{property.Name}", result);
            }
            return;
        }
        if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
            {
                AddJsonLeaves(item, $"{path}:{index++}", result);
            }
            return;
        }
        result[path] = element.GetRawText();
    }

    private static LineDifference LineMultisetDifference(string baseline, string target)
    {
        var baselineLines = Lines(baseline).ToList();
        var targetLines = Lines(target).ToList();
        foreach (var line in baselineLines.ToArray())
        {
            var targetIndex = targetLines.IndexOf(line);
            if (targetIndex < 0)
            {
                continue;
            }
            baselineLines.Remove(line);
            targetLines.RemoveAt(targetIndex);
        }
        return new LineDifference(
            baselineLines.Order(StringComparer.Ordinal).ToArray(),
            targetLines.Order(StringComparer.Ordinal).ToArray());
    }

    private static IEnumerable<string> Lines(string value) => value
        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
        .Select(line => line.Trim())
        .Where(line => line.Length > 0);

    private static IEnumerable<ParsedSource> Parsed(
        IReadOnlyList<RefactorSourceReader.SourceFile> files,
        Func<RefactorSourceReader.SourceFile, bool> predicate) =>
        files.Where(predicate).Select(file => new ParsedSource(
            file,
            CSharpSyntaxTree.ParseText(
                    file.Content,
                    CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest))
                .GetCompilationUnitRoot()));

    private static string InvocationName(InvocationExpressionSyntax invocation) =>
        invocation.Expression switch
        {
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            GenericNameSyntax generic => generic.Identifier.ValueText,
            _ => string.Empty
        };

    private static string SerializationAttributeName(AttributeSyntax attribute) =>
        attribute.Name.ToString().Split('.').Last().Replace("Attribute", string.Empty, StringComparison.Ordinal);

    private static string Normalize(SyntaxNode node) =>
        string.Concat(node.DescendantTokens().Select(token => token.Text));

    private static void AssertExact(string contract, IReadOnlyList<string> baseline, IReadOnlyList<string> target)
    {
        var difference = MultisetDifference(baseline, target);
        Assert.True(
            difference.Removed.Count == 0 && difference.Added.Count == 0,
            $"{contract} changed. Baseline={baseline.Count}, target={target.Count}. "
            + $"Missing=[{string.Join(", ", difference.Removed.Take(3))}] "
            + $"Added=[{string.Join(", ", difference.Added.Take(3))}]");
    }

    private static void AssertExactWithAllowedAdditions(
        string contract,
        IReadOnlyList<string> baseline,
        IReadOnlyList<string> target,
        IReadOnlyList<string> allowedAdditions)
    {
        var difference = MultisetDifference(baseline, target);
        var unexplained = MultisetDifference(allowedAdditions, difference.Added);
        Assert.True(
            difference.Removed.Count == 0
            && unexplained.Removed.Count == 0
            && unexplained.Added.Count == 0,
            $"{contract} changed outside the accepted additions. "
            + $"Missing=[{string.Join(", ", difference.Removed.Take(3))}] "
            + $"Unexpected=[{string.Join(", ", unexplained.Added.Take(3))}] "
                + $"UnobservedAccepted=[{string.Join(", ", unexplained.Removed.Take(3))}]");
    }

    private static void AssertExactWithAllowedReplacements(
        string contract,
        IReadOnlyList<string> baseline,
        IReadOnlyList<string> target,
        IReadOnlyList<string> allowedRemoved,
        IReadOnlyList<string> allowedAdded)
    {
        var difference = MultisetDifference(baseline, target);
        var unexplainedRemoved = MultisetDifference(allowedRemoved, difference.Removed);
        var unexplainedAdded = MultisetDifference(allowedAdded, difference.Added);
        Assert.True(
            unexplainedRemoved.Removed.Count == 0
            && unexplainedRemoved.Added.Count == 0
            && unexplainedAdded.Removed.Count == 0
            && unexplainedAdded.Added.Count == 0,
            $"{contract} changed outside the accepted replacements. "
                + $"Missing=[{string.Join(", ", unexplainedRemoved.Added.Take(3))}] "
                + $"Unexpected=[{string.Join(", ", unexplainedAdded.Added.Take(3))}] "
                + $"UnobservedRemoved=[{string.Join(", ", unexplainedRemoved.Removed.Take(3))}] "
                + $"UnobservedAdded=[{string.Join(", ", unexplainedAdded.Removed.Take(3))}]");
    }

    private static LineDifference MultisetDifference(
        IEnumerable<string> baseline,
        IEnumerable<string> target)
    {
        var baselineItems = baseline.ToList();
        var targetItems = target.ToList();
        foreach (var item in baselineItems.ToArray())
        {
            var targetIndex = targetItems.IndexOf(item);
            if (targetIndex < 0)
            {
                continue;
            }
            baselineItems.Remove(item);
            targetItems.RemoveAt(targetIndex);
        }
        return new LineDifference(baselineItems, targetItems);
    }

    private sealed record ParsedSource(
        RefactorSourceReader.SourceFile File,
        CompilationUnitSyntax Root);

    private sealed record LineDifference(
        IReadOnlyList<string> Removed,
        IReadOnlyList<string> Added);
}
