using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Zumbo.ArchitectureTests;

public sealed class ModuleFirstSourceLayoutTests
{
    private static readonly Regex DomainDependencyPattern = new(
        "Microsoft\\.AspNetCore|MongoDB\\.|Npgsql|StackExchange\\.Redis|SignalR|OpenSearch|Minio|Amazon\\.S3|\\bHttpClient\\b|System\\.IO|Zumbo\\.[A-Za-z0-9_.]*Infrastructure|Zumbo\\.Api|Zumbo\\.BuildingBlocks\\.Application",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex ApplicationDependencyPattern = new(
        DomainDependencyPattern + "|\\b[A-Za-z_]\\w*Document\\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex ContractConcreteTypePattern = new(
        "IDocumentRepository|\\b[A-Za-z_]\\w*Repository\\b|MongoDB\\.|Npgsql|StackExchange\\.Redis|\\b[A-Za-z_]\\w*Service\\b|\\b[A-Za-z_]\\w*Document\\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex EndpointPersistencePattern = new(
        "IDocumentRepository|\\b[A-Za-z_]\\w*Document\\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex ModuleHttpTypePattern = new(
        "\\bIResult\\b|\\bHttpContext\\b|Microsoft\\.AspNetCore|\\[(?:Authorize|AllowAnonymous|FromBody|FromRoute|FromQuery|HttpGet|HttpPost|HttpPut|HttpPatch|HttpDelete)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex MethodPartialFilePattern = new(
        "^[^.]+\\.(?<fragment>[A-Z][A-Za-z0-9]*(?:Async)?)\\.cs$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex PartialDeclarationPattern = new(
        "\\bpartial\\s+(?:(?:sealed|static|abstract)\\s+)*(?:class|record|struct|interface)\\s+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly string[] StandardLayers =
        ["Domain", "Application", "Contracts", "Infrastructure", "Composition", "Presentation"];

    private static string BackendDirectory => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static string ProjectDirectory => Directory.GetParent(BackendDirectory)!.FullName;

    private static string SourceDirectory => Path.Combine(BackendDirectory, "src");

    private static string ArchitectureDirectory => Path.Combine(ProjectDirectory, "docs", "architecture");

    [Fact]
    public void Domain_DoesNotDependOnApplicationInfrastructureApiOrProviders()
    {
        AssertMatchesAllowList(
            "domain-dependencies",
            FindTextViolations(ModuleLayerFiles("Domain"), DomainDependencyPattern));
    }

    [Fact]
    public void Application_DoesNotDependOnInfrastructureApiProvidersOrDocuments()
    {
        AssertMatchesAllowList(
            "application-dependencies",
            FindTextViolations(ModuleLayerFiles("Application"), ApplicationDependencyPattern));
    }

    [Fact]
    public void Contracts_DoNotContainConcreteServicesRepositoriesDocumentsOrProviders()
    {
        AssertMatchesAllowList(
            "contracts-concrete-types",
            FindTextViolations(ModuleLayerFiles("Contracts"), ContractConcreteTypePattern));
    }

    [Fact]
    public void Endpoints_DoNotAccessRepositoriesOrPersistenceDocuments()
    {
        var endpointFiles = ProductionSourceFiles()
            .Where(path =>
            {
                var relative = RelativePath(path);
                return relative.StartsWith("Zumbo.Api/Endpoints/", StringComparison.Ordinal)
                    || relative.StartsWith("Zumbo.Api/Presentation/Endpoints/", StringComparison.Ordinal);
            });

        AssertMatchesAllowList(
            "endpoint-persistence",
            FindTextViolations(endpointFiles, EndpointPersistencePattern));
    }

    [Fact]
    public void Modules_DoNotReferenceOtherModuleProjects()
    {
        var violations = new List<string>();
        foreach (var moduleDirectory in Directory.GetDirectories(SourceDirectory, "Zumbo.Modules.*"))
        {
            var projectFile = Directory.GetFiles(moduleDirectory, "*.csproj").Single();
            var moduleName = Path.GetFileNameWithoutExtension(projectFile);
            var references = XDocument.Load(projectFile)
                .Descendants()
                .Where(element => element.Name.LocalName == "ProjectReference")
                .Select(element => element.Attribute("Include")?.Value)
                .OfType<string>()
                .Select(reference => Path.GetFileNameWithoutExtension(reference.Replace('\\', '/')));

            if (references.Any(reference =>
                reference.StartsWith("Zumbo.Modules.", StringComparison.Ordinal)
                && !string.Equals(reference, moduleName, StringComparison.Ordinal)
                && !reference.EndsWith(".Contracts", StringComparison.Ordinal)))
            {
                violations.Add(RelativePath(projectFile));
            }
        }

        AssertMatchesAllowList("cross-module-project-references", violations);
    }

    [Fact]
    public void BuildingBlocks_DoesNotOwnModuleSpecificTypes()
    {
        var violations = ProductionSourceFiles()
            .Select(path => new { Path = path, Relative = RelativePath(path) })
            .Where(item =>
                Regex.IsMatch(
                    item.Relative,
                    "^Zumbo\\.BuildingBlocks\\.(?:Application|Infrastructure)/Search/(?:.*/)?WorkItemSearch/",
                    RegexOptions.CultureInvariant)
                || Regex.IsMatch(
                    item.Relative,
                    "^Zumbo\\.BuildingBlocks\\.(?:Application|Infrastructure)/Security/Contracts/IdentitySecurity/",
                    RegexOptions.CultureInvariant)
                || Regex.IsMatch(
                    item.Relative,
                    "^Zumbo\\.BuildingBlocks\\.(?:Application|Infrastructure)/Security/ProjectResourcePolicy/",
                    RegexOptions.CultureInvariant)
                || item.Relative == "Zumbo.BuildingBlocks.Application/Security/PermissionCatalog.cs")
            .Select(item => item.Relative);

        AssertMatchesAllowList("building-blocks-module-types", violations);
    }

    [Fact]
    public void PersistenceDocuments_DoNotLiveUnderDomain()
    {
        var violations = ModuleLayerFiles("Domain")
            .Where(path => Path.GetFileNameWithoutExtension(path).EndsWith("Document", StringComparison.Ordinal))
            .Select(RelativePath);

        AssertMatchesAllowList("domain-documents", violations);
    }

    [Fact]
    public void ModuleDomainAndApplication_DoNotUseAspNetHttpTypes()
    {
        AssertMatchesAllowList(
            "module-http-types",
            FindTextViolations(
                ModuleLayerFiles("Domain").Concat(ModuleLayerFiles("Application")),
                ModuleHttpTypePattern));
    }

    [Fact]
    public void PartialOwners_DoNotExceedEightFiles()
    {
        var declarations = ProductionSourceFiles()
            .SelectMany(path =>
            {
                var relative = RelativePath(path);
                var project = relative.Split('/')[0];
                var root = Parse(path);
                return root.DescendantNodes()
                    .OfType<TypeDeclarationSyntax>()
                    .Where(declaration => declaration.Modifiers.Any(
                        modifier => modifier.RawKind == (int)SyntaxKind.PartialKeyword))
                    .Select(declaration => new
                    {
                        Key = $"{project}|{declaration.Identifier.ValueText}",
                        Path = relative
                    });
            });

        var violations = declarations
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .Where(group => group.Count() > 8)
            .SelectMany(group => group.Select(item => item.Path))
            .Distinct(StringComparer.Ordinal);

        AssertMatchesAllowList("partial-part-limit", violations);
    }

    [Fact]
    public void MethodNamedPartialFiles_AreNotUsed()
    {
        var violations = ProductionSourceFiles()
            .Where(path => PartialDeclarationPattern.IsMatch(File.ReadAllText(path)))
            .Where(path =>
            {
                var match = MethodPartialFilePattern.Match(Path.GetFileName(path));
                if (!match.Success)
                {
                    return false;
                }

                var fragmentName = match.Groups["fragment"].Value;
                return Parse(path).DescendantNodes()
                    .OfType<MethodDeclarationSyntax>()
                    .Any(method => method.Identifier.ValueText.Equals(fragmentName, StringComparison.Ordinal));
            })
            .Select(RelativePath);

        AssertMatchesAllowList("method-partial-files", violations);
    }

    [Fact]
    public void Namespace_MatchesStandardizedLayerDirectoryAndMoveMapCoversSource()
    {
        var violations = new List<string>();
        foreach (var path in ProductionSourceFiles())
        {
            var relative = RelativePath(path);
            var parts = relative.Split('/');
            if (parts.Length < 3 || !StandardLayers.Contains(parts[1], StringComparer.Ordinal))
            {
                continue;
            }

            var expected = string.Join('.', parts[..^1]);
            var actual = Parse(path).Members
                .OfType<BaseNamespaceDeclarationSyntax>()
                .Select(declaration => declaration.Name.ToString())
                .FirstOrDefault() ?? string.Empty;
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                violations.Add(relative);
            }
        }

        AssertMatchesAllowList("namespace-directory", violations);

        var mapPath = Path.Combine(ArchitectureDirectory, "SOURCE_MOVE_MAP.csv");
        var mappedPaths = File.ReadLines(mapPath)
            .Skip(1)
            .Select(line => Regex.Match(line, "^\"([^\"]+)\"").Groups[1].Value)
            .Where(path => path.Length > 0)
            .Select(path => path["Backend/src/".Length..])
            .Order(StringComparer.Ordinal)
            .ToArray();
        var sourcePaths = ProductionSourceFiles().Select(RelativePath).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(sourcePaths, mappedPaths);
    }

    [Fact]
    public void ProductionSourcePaths_StayWithinTwoHundredCharacters()
    {
        AssertMatchesAllowList(
            "path-length",
            ProductionSourceFiles()
                .Where(path => path.Length > 200)
                .Select(RelativePath));
    }

    [Fact]
    public void PreservationManifest_RetainsAllBaselineTypesMembersAndBodies()
    {
        using var report = ReadJson("refactor-unmatched-elements.json");
        var root = report.RootElement;
        Assert.True(root.GetProperty("passed").GetBoolean());
        Assert.Empty(root.GetProperty("MissingTypes").EnumerateArray());
        Assert.Empty(root.GetProperty("TypeSignatureDifferences").EnumerateArray());
        Assert.Empty(root.GetProperty("MissingMembers").EnumerateArray());
        Assert.Empty(root.GetProperty("MemberSignatureDifferences").EnumerateArray());
        Assert.Empty(root.GetProperty("unexplainedBodyDifferences").EnumerateArray());
        Assert.Equal(1208, root.GetProperty("counts").GetProperty("baselineTypes").GetInt32());
        Assert.Equal(5072, root.GetProperty("counts").GetProperty("matchedMembers").GetInt32());
    }

    [Fact]
    public void EndpointRouteInventory_RemainsExact()
    {
        using var report = ReadJson("refactor-runtime-contracts.json");
        var root = report.RootElement;
        Assert.True(root.GetProperty("passed").GetBoolean());
        Assert.Equal(319, root.GetProperty("exactContractCounts").GetProperty("endpoints").GetInt32());
        Assert.Empty(root.GetProperty("missingContracts").EnumerateArray());
        Assert.Empty(root.GetProperty("changedContracts").EnumerateArray());
    }

    [Fact]
    public void DependencyInjectionAndHostedServiceInventory_RemainsExact()
    {
        using var report = ReadJson("refactor-runtime-contracts.json");
        Assert.Equal(
            274,
            report.RootElement.GetProperty("exactContractCounts").GetProperty("registrations").GetInt32());

        var registration = ProductionSourceFiles()
            .Select(File.ReadAllText)
            .Count(source => source.Contains(
                "AddHostedService<AutomationRuntimeHostedService>()",
                StringComparison.Ordinal));
        Assert.Equal(1, registration);
    }

    [Fact]
    public void PostgreSqlMongoAndSerializationContracts_RemainExact()
    {
        using var report = ReadJson("refactor-runtime-contracts.json");
        var counts = report.RootElement.GetProperty("exactContractCounts");
        Assert.Equal(37, counts.GetProperty("migrations").GetInt32());
        Assert.Equal(40, counts.GetProperty("mongo").GetInt32());
        Assert.Equal(1, counts.GetProperty("serialization").GetInt32());
        Assert.Equal(191, counts.GetProperty("messaging").GetInt32());
        Assert.Empty(report.RootElement.GetProperty("missingContracts").EnumerateArray());
        Assert.Empty(report.RootElement.GetProperty("changedContracts").EnumerateArray());
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
            .ToArray();

    private static IEnumerable<string> ModuleLayerFiles(string layer) =>
        ProductionSourceFiles().Where(path =>
        {
            var parts = RelativePath(path).Split('/');
            return parts.Length >= 3
                && parts[0].StartsWith("Zumbo.Modules.", StringComparison.Ordinal)
                && string.Equals(parts[1], layer, StringComparison.Ordinal);
        });

    private static IEnumerable<string> FindTextViolations(
        IEnumerable<string> files,
        Regex pattern) =>
        files.Where(path => pattern.IsMatch(File.ReadAllText(path))).Select(RelativePath);

    private static CompilationUnitSyntax Parse(string path) =>
        CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetCompilationUnitRoot();

    private static string RelativePath(string path) =>
        Path.GetRelativePath(SourceDirectory, path).Replace('\\', '/');

    private static JsonDocument ReadJson(string fileName) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(ArchitectureDirectory, fileName)));

    private static void AssertMatchesAllowList(string ruleName, IEnumerable<string> actualPaths)
    {
        var document = ReadAllowList();
        Assert.Equal(1, document.SchemaVersion);
        Assert.Equal("99c206b3765c6513df5e3415ec0d1d44b1465147", document.BaselineCommit);
        Assert.True(document.Rules.TryGetValue(ruleName, out var rule), $"Missing allow-list rule: {ruleName}");
        Assert.False(string.IsNullOrWhiteSpace(rule!.Reason));
        Assert.Matches("^A[2-9](?:-A?[2-9])?$", rule.RemovalStage);

        var expected = rule.Paths.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var actual = actualPaths.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var stale = expected.Except(actual, StringComparer.Ordinal).ToArray();
        var unexpected = actual.Except(expected, StringComparer.Ordinal).ToArray();

        Assert.True(
            stale.Length == 0 && unexpected.Length == 0,
            $"Rule '{ruleName}' differs from its exact path allow-list."
                + Environment.NewLine
                + $"Stale entries:{Environment.NewLine}{string.Join(Environment.NewLine, stale)}"
                + Environment.NewLine
                + $"Unexpected violations:{Environment.NewLine}{string.Join(Environment.NewLine, unexpected)}");
    }

    private static AllowListDocument ReadAllowList()
    {
        var path = Path.Combine(ArchitectureDirectory, "module-first-architecture-allowlist.json");
        return JsonSerializer.Deserialize<AllowListDocument>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Module-first architecture allow-list is invalid.");
    }

    private sealed class AllowListDocument
    {
        public int SchemaVersion { get; init; }

        public string BaselineCommit { get; init; } = string.Empty;

        public Dictionary<string, AllowListRule> Rules { get; init; } = new(StringComparer.Ordinal);
    }

    private sealed class AllowListRule
    {
        public string Reason { get; init; } = string.Empty;

        public string RemovalStage { get; init; } = string.Empty;

        public string[] Paths { get; init; } = [];
    }
}
