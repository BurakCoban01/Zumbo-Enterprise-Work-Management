using Zumbo.BuildingBlocks.Infrastructure.Security;
using Zumbo.Capacity;

namespace Zumbo.UnitTests;

public sealed class CapacityToolTests
{
    [Fact]
    public void ProfileUsesSafeRunScopedDeterministicIdentity()
    {
        var first = CapacityProfile.Resolve("smoke", "ops006-a1");
        var second = CapacityProfile.Resolve("smoke", "ops006-a1");
        var other = CapacityProfile.Resolve("smoke", "ops006-a2");

        Assert.Equal("capacity-smoke-ops006-a1-", first.Prefix);
        Assert.Equal(first.SeedTimestamp, second.SeedTimestamp);
        Assert.NotEqual(first.Prefix, other.Prefix);
        Assert.NotEqual(first.SeedTimestamp, other.SeedTimestamp);
        Assert.Contains(first.RunId, CapacityIds.Username(first, 0), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("-edge")]
    [InlineData("edge-")]
    [InlineData("unsafe/path")]
    public void ProfileRejectsUnsafeRunIdentity(string runId)
    {
        Assert.Throws<ArgumentException>(() => CapacityProfile.Resolve("smoke", runId));
    }

    [Fact]
    public void PercentilesAndOperationMixAreDeterministic()
    {
        var values = Enumerable.Range(1, 100).Select(x => (double)x).ToArray();
        Assert.Equal(50, CapacityMath.Percentile(values, 0.50));
        Assert.Equal(95, CapacityMath.Percentile(values, 0.95));
        Assert.Equal(99, CapacityMath.Percentile(values, 0.99));

        var mix = Enumerable.Range(0, 20).Select(x => CapacityMath.OperationFor(x)).ToArray();
        Assert.Equal(8, mix.Count(x => x == "read"));
        Assert.Equal(4, mix.Count(x => x == "search"));
        Assert.Equal(4, mix.Count(x => x == "report"));
        Assert.Equal(2, mix.Count(x => x == "write"));
        Assert.Single(mix, x => x == "external");
        Assert.Single(mix, x => x == "upload");
    }

    [Fact]
    public void DeterministicCapacityHashUsesProductionPbkdf2Contract()
    {
        var profile = CapacityProfile.Resolve("smoke", "ops006-hash");
        var first = CapacityMath.CreateDeterministicPasswordHash("synthetic-capacity-password", profile);
        var second = CapacityMath.CreateDeterministicPasswordHash("synthetic-capacity-password", profile);

        Assert.Equal(first, second);
        Assert.True(new Pbkdf2PasswordHasher().Verify("synthetic-capacity-password", first));
        Assert.False(new Pbkdf2PasswordHasher().Verify("wrong-password", first));
    }

    [Fact]
    public void ScenariosCoverBoundedLoadSpikeAndSoak()
    {
        var scenarios = ScenarioRunner.Definitions(CapacityProfile.Resolve("smoke", "ops006-scenarios"));

        Assert.Equal(["load", "spike", "soak"], scenarios.Select(x => x.Name));
        Assert.All(scenarios, scenario =>
        {
            Assert.InRange(scenario.Duration, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(5));
            Assert.True(scenario.Concurrency > 0);
            Assert.True(scenario.P99BudgetMilliseconds >= scenario.P95BudgetMilliseconds);
            Assert.InRange(scenario.MaximumErrorRate, 0, 0.05);
        });
    }

    [Fact]
    public void CapacityOrchestratorEnforcesPreflightEvidenceAndTargetedCleanup()
    {
        var backendRoot = FindBackendRoot();
        var script = File.ReadAllText(Path.Combine(backendRoot, "scripts", "Invoke-CapacityGate.ps1"));
        var compose = File.ReadAllText(Path.Combine(backendRoot, "docker-compose.capacity.yml"));

        foreach (var port in Enumerable.Range(59117, 6))
        {
            Assert.Contains(port.ToString(), script, StringComparison.Ordinal);
        }
        Assert.Contains("Test-PortCanBind", script, StringComparison.Ordinal);
        Assert.Contains("MinimumFreeMemoryMiB", script, StringComparison.Ordinal);
        Assert.Contains("MinimumFreeDiskGiB", script, StringComparison.Ordinal);
        Assert.Contains("RandomNumberGenerator", script, StringComparison.Ordinal);
        Assert.Contains("Invoke-Capacity \"clean\" $RunId", script, StringComparison.Ordinal);
        Assert.Contains("Invoke-Capacity \"clean\" $degradedRunId", script, StringComparison.Ordinal);
        Assert.Contains("label=com.docker.compose.project=$ProjectName", script, StringComparison.Ordinal);
        Assert.DoesNotContain("system prune", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("${ZUMBO_CAPACITY_API_IMAGE:-zumbo-capacity-api:local}", compose, StringComparison.Ordinal);
    }

    private static string FindBackendRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Zumbo.sln")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Backend root was not found from the test output directory.");
    }
}
