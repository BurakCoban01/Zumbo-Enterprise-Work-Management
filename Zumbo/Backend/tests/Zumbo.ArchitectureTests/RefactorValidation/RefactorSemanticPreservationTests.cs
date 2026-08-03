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
            ["Zumbo.Persistence.PostgreSql|Zumbo.Persistence.PostgreSql.PostgreSqlMigrationRunner|method:BuildMigrations:():IReadOnlyList<Migration>"] =
                "Migration SQL constants moved from one method body to private fields while the ordered 37 Migration.Create calls remain in BuildMigrations; IDs, names, SQL, and checksums are verified by the runtime contract audit.",
            ["Zumbo.BuildingBlocks.Infrastructure|Zumbo.BuildingBlocks.Infrastructure.Search.OpenSearchWorkItemSearchIndex|method:InitializeAsync:(CancellationTokencancellationToken=default):Task"] =
                "Initialization now detects the pre-alias concrete index layout and delegates to a lossless, count-verified reindex migration before atomically installing the write alias; unit and live runtime checks cover both success and fail-closed behavior.",
            ["Zumbo.Api|MongoMigrationRunner|method:ApplyIndexesAsync:(stringmigrationId,IReadOnlyList<MongoIndexSpecification>indexes,CancellationTokencancellationToken):Task<MongoMigrationOutcome>"] =
                "Index application preserves catalog checksums while accepting semantically equivalent legacy names and skipping superseded identity and notification definitions whose dedicated later migrations own the valid replacements.",
            ["Zumbo.Api|MongoMigrationRunner|method:RunAsync:(CancellationTokencancellationToken=default):Task<MongoMigrationRunReport>"] =
                "Startup now always runs idempotent compatibility migrations that normalize legacy user document versions and remove infrastructure-only migration markers; optional high-volume business backfills remain explicitly gated."
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
