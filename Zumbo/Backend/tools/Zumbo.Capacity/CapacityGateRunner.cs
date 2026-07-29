using System.Runtime.InteropServices;
using Zumbo.BuildingBlocks.Application.Runtime;

namespace Zumbo.Capacity;

internal sealed class CapacityGateRunner(
    string mongoConnectionString,
    string openSearchBaseUrl,
    string apiBaseUrl,
    string capacityPassword)
{
    public async Task<CapacityGateResult> RunAsync(CapacityProfile profile, CancellationToken ct)
    {
        var seedRunner = new SeedRunner(mongoConnectionString, openSearchBaseUrl, capacityPassword);
        var freeBefore = CapacityMath.GetDiskFreeBytes();
        var maximumDatasetBytes = profile.Name switch
        {
            "smoke" => 256L * 1024 * 1024,
            "demo" => 2L * 1024 * 1024 * 1024,
            _ => 4L * 1024 * 1024 * 1024
        };
        if (freeBefore < maximumDatasetBytes + 512L * 1024 * 1024)
        {
            throw new InvalidOperationException("Capacity disk preflight failed for the selected profile.");
        }
        var storageBefore = await CapacityStorageProbe.MeasureAsync(
            mongoConnectionString,
            openSearchBaseUrl,
            ct);

        SeedResult? seed = null;
        BenchmarkResult? benchmark = null;
        IReadOnlyList<ScenarioResult> scenarios = [];
        IReadOnlyList<QueryPlanResult> plans = [];
        var freeAfterSeed = freeBefore;
        var storageAfterSeed = storageBefore;
        var stage = "seed";
        string? errorStage = null;
        string? errorType = null;
        var cleanup = new CleanupResult(profile.Name, profile.RunId, profile.Prefix, -1, -1, false);
        var cleanupTimeout = profile.Name switch
        {
            "smoke" => TimeSpan.FromSeconds(30),
            "demo" => TimeSpan.FromMinutes(1),
            _ => TimeSpan.FromMinutes(2)
        };
        try
        {
            seed = await seedRunner.RunAsync(profile, ct);
            freeAfterSeed = CapacityMath.GetDiskFreeBytes();
            storageAfterSeed = await CapacityStorageProbe.MeasureAsync(
                mongoConnectionString,
                openSearchBaseUrl,
                ct);
            stage = "query-plan";
            plans = await new QueryPlanRunner(mongoConnectionString).RunAsync(profile, ct);
            stage = "benchmark";
            benchmark = await new BenchmarkRunner(mongoConnectionString, apiBaseUrl, capacityPassword)
                .RunAsync(profile, 10, profile.RealtimeClientCount, ct);
            var scenarioRunner = new ScenarioRunner(mongoConnectionString, apiBaseUrl, capacityPassword);
            var captured = new List<ScenarioResult>();
            foreach (var definition in ScenarioRunner.Definitions(profile))
            {
                stage = definition.Name;
                captured.Add(await scenarioRunner.RunAsync(profile, definition, ct));
            }
            scenarios = captured;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            errorStage = stage;
            errorType = exception.GetType().Name;
        }
        finally
        {
            var cleanupExecution = await CompensationExecution.RunAsync(
                "capacity.seed.cleanup",
                async token =>
                {
                    cleanup = await seedRunner.CleanAsync(profile, token);
                },
                cleanupTimeout);
            if (!cleanupExecution.Succeeded)
            {
                errorStage ??= "cleanup";
                errorType ??= cleanupExecution.Exception?.GetType().Name
                    ?? cleanupExecution.Outcome.ToString();
            }
        }

        var freeAfterCleanup = CapacityMath.GetDiskFreeBytes();
        var storageAfterCleanup = storageAfterSeed;
        var probeExecution = await CompensationExecution.RunAsync(
            "capacity.storage_cleanup.probe",
            async token =>
            {
                storageAfterCleanup = await CapacityStorageProbe.MeasureAsync(
                    mongoConnectionString,
                    openSearchBaseUrl,
                    token);
            },
            cleanupTimeout);
        if (!probeExecution.Succeeded)
        {
            errorStage ??= "cleanup-probe";
            errorType ??= probeExecution.Exception?.GetType().Name
                ?? probeExecution.Outcome.ToString();
        }
        var datasetBytes = Math.Max(0, storageAfterSeed - storageBefore);
        var disk = new DiskUsageResult(
            freeBefore,
            freeAfterSeed,
            freeAfterCleanup,
            storageBefore,
            storageAfterSeed,
            storageAfterCleanup,
            datasetBytes,
            maximumDatasetBytes,
            datasetBytes <= maximumDatasetBytes);
        var machine = new ReferenceMachine(
            Environment.MachineName,
            RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString(),
            Environment.ProcessorCount,
            GC.GetGCMemoryInfo().TotalAvailableMemoryBytes);
        var passed = errorType is null
            && seed is not null
            && benchmark?.AllBudgetsPassed == true
            && scenarios.Count == 3
            && scenarios.All(x => x.Passed)
            && plans.Count == 2
            && plans.All(x => x.Passed)
            && disk.Passed
            && cleanup.Passed;
        return new CapacityGateResult(
            DateTimeOffset.UtcNow,
            profile.Name,
            profile.RunId,
            profile.Prefix,
            machine,
            seed,
            benchmark,
            scenarios,
            plans,
            disk,
            cleanup,
            errorStage,
            errorType,
            passed);
    }
}
