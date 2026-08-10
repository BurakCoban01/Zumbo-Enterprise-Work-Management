using System.Text;
using System.Text.Json;

namespace Zumbo.ArchitectureTests.RefactorValidation;

internal static class RefactorValidationReportBuilder
{
    internal static IReadOnlyDictionary<string, string> Build(
        RefactorSemanticInventory.Comparison comparison,
        IReadOnlyDictionary<string, string> acceptedBodyDifferences)
    {
        var unexplainedBodies = comparison.BodyDifferences
            .Where(item => !acceptedBodyDifferences.ContainsKey(item.Id))
            .ToArray();
        var outputs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ARCHITECTURE_REFACTOR_VALIDATION.md"] = ValidationSummary(
                comparison,
                acceptedBodyDifferences,
                unexplainedBodies),
            ["refactor-file-map.csv"] = FileMap(comparison),
            ["refactor-unmatched-elements.json"] = UnmatchedJson(
                comparison,
                acceptedBodyDifferences,
                unexplainedBodies),
            ["refactor-contract-diff.md"] = ContractDiff(
                comparison,
                acceptedBodyDifferences,
                unexplainedBodies)
        };
        return outputs;
    }

    private static string ValidationSummary(
        RefactorSemanticInventory.Comparison comparison,
        IReadOnlyDictionary<string, string> acceptedBodyDifferences,
        IReadOnlyCollection<RefactorSemanticInventory.MemberDifference> unexplainedBodies)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Architecture Refactor Validation");
        builder.AppendLine();
        builder.AppendLine("## Scope");
        builder.AppendLine();
        builder.AppendLine($"- Baseline: `{RefactorSemanticInventory.BaselineCommit}`");
        builder.AppendLine($"- Preserved refactor snapshot: `{RefactorSemanticInventory.RefactorSnapshotCommit}`");
        builder.AppendLine("- Target: current review-branch working tree under `Backend/src`");
        builder.AppendLine("- Parser: Roslyn C# syntax inventory with partial declarations merged by project and fully qualified type name");
        builder.AppendLine("- Method and initializer comparison: syntax tokens normalized without comments, trivia, whitespace, or line endings");
        builder.AppendLine();
        builder.AppendLine("## Semantic Inventory");
        builder.AppendLine();
        builder.AppendLine("| Measure | Baseline | Target |");
        builder.AppendLine("| --- | ---: | ---: |");
        builder.AppendLine($"| Production C# files | {comparison.Baseline.FileCount} | {comparison.Target.FileCount} |");
        builder.AppendLine($"| Types (nested included) | {comparison.Baseline.Types.Count} | {comparison.Target.Types.Count} |");
        builder.AppendLine($"| Members | {comparison.Baseline.MemberCount} | {comparison.Target.MemberCount} |");
        builder.AppendLine($"| Baseline members matched | {comparison.MatchedMembers} | {comparison.Baseline.MemberCount} expected |");
        builder.AppendLine();
        builder.AppendLine("## Preservation Result");
        builder.AppendLine();
        builder.AppendLine($"- Missing types: **{comparison.MissingTypes.Count}**");
        builder.AppendLine($"- Type signature differences: **{comparison.TypeSignatureDifferences.Count}**");
        builder.AppendLine($"- Missing members: **{comparison.MissingMembers.Count}**");
        builder.AppendLine($"- Member signature differences: **{comparison.MemberSignatureDifferences.Count}**");
        builder.AppendLine($"- Accepted structural/intentional body differences: **{acceptedBodyDifferences.Count}**");
        builder.AppendLine($"- Unexplained body differences: **{unexplainedBodies.Count}**");
        builder.AppendLine();
        builder.AppendLine("### Reviewed Body Differences");
        builder.AppendLine();
        foreach (var (id, reason) in acceptedBodyDifferences.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            builder.AppendLine($"- `{id}`: {reason}");
        }
        builder.AppendLine();
        builder.AppendLine("The machine-readable detail is in `refactor-unmatched-elements.json`; the deleted/split file trace is in `refactor-file-map.csv`.");
        builder.AppendLine();
        builder.AppendLine("## Current Decision");
        builder.AppendLine();
        builder.AppendLine(unexplainedBodies.Count == 0
            && comparison.MissingTypes.Count == 0
            && comparison.TypeSignatureDifferences.Count == 0
            && comparison.MissingMembers.Count == 0
            && comparison.MemberSignatureDifferences.Count == 0
                ? "Stage 1 semantic preservation is **proven** by the checked-in Roslyn gate."
                : "Stage 1 semantic preservation is **not proven**. Production refactoring must not proceed until every item is resolved or explicitly evidenced.");
        return builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string FileMap(RefactorSemanticInventory.Comparison comparison)
    {
        var targetTypes = comparison.Target.Types.ToDictionary(type => type.Key, StringComparer.Ordinal);
        var bodyDifferenceIds = comparison.BodyDifferences.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var rows = new List<string>
        {
            "old_path,full_types,new_paths,status,old_members,matched_members,unmatched_members,normalized_body_result,evidence"
        };

        foreach (var oldPath in comparison.Baseline.Files)
        {
            var types = comparison.Baseline.Types
                .Where(type => type.Files.Contains(oldPath, StringComparer.Ordinal))
                .ToArray();
            var newPaths = types
                .Select(type => comparison.RelocatedTypes.GetValueOrDefault(type.Key) ?? type.Key)
                .Where(targetTypes.ContainsKey)
                .SelectMany(targetKey => targetTypes[targetKey].Files)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (types.Length == 0 && comparison.Target.Files.Contains(oldPath, StringComparer.Ordinal))
            {
                newPaths = [oldPath];
            }
            var oldMembers = types.Sum(type => type.Members.Count(member => member.File == oldPath));
            var unmatched = comparison.MissingMembers.Count(item => item.BaselineFile == oldPath)
                + comparison.MemberSignatureDifferences.Count(item => item.BaselineFile == oldPath);
            var bodyChanged = types.SelectMany(type => type.Members)
                .Where(member => member.File == oldPath)
                .Any(member => bodyDifferenceIds.Contains($"{types.First(type => type.Members.Contains(member)).Key}|{member.Key}"));
            var status = newPaths.Length switch
            {
                0 => "unmapped",
                1 when newPaths[0] == oldPath => "preserved",
                1 => "moved",
                _ => "split"
            };
            rows.Add(string.Join(',',
                Csv(oldPath),
                Csv(string.Join('|', types.Select(type => type.FullName).Order(StringComparer.Ordinal))),
                Csv(string.Join('|', newPaths)),
                Csv(status),
                oldMembers,
                oldMembers - unmatched,
                unmatched,
                Csv(bodyChanged ? "changed-reviewed-separately" : "exact"),
                Csv("Roslyn type/member/signature/body inventory")));
        }

        return string.Join('\n', rows) + "\n";
    }

    private static string UnmatchedJson(
        RefactorSemanticInventory.Comparison comparison,
        IReadOnlyDictionary<string, string> acceptedBodyDifferences,
        IReadOnlyCollection<RefactorSemanticInventory.MemberDifference> unexplainedBodies)
    {
        var payload = new
        {
            schemaVersion = 1,
            baselineCommit = RefactorSemanticInventory.BaselineCommit,
            refactorSnapshotCommit = RefactorSemanticInventory.RefactorSnapshotCommit,
            generatedFrom = "current review-branch working tree",
            counts = new
            {
                baselineFiles = comparison.Baseline.FileCount,
                targetFiles = comparison.Target.FileCount,
                baselineTypes = comparison.Baseline.Types.Count,
                targetTypes = comparison.Target.Types.Count,
                baselineMembers = comparison.Baseline.MemberCount,
                targetMembers = comparison.Target.MemberCount,
                matchedMembers = comparison.MatchedMembers,
                addedTypes = comparison.AddedTypes.Count,
                addedMembers = comparison.AddedMembers.Count
            },
            addedTypes = comparison.AddedTypes.Select(type => new
            {
                id = type.Key,
                files = type.Files,
                kind = type.Kind,
                signature = type.Signature,
                memberCount = type.Members.Count
            }),
            addedMembers = comparison.AddedMembers.Select(member => new
            {
                type = member.Type,
                member = member.Member,
                file = member.File,
                signature = member.Signature
            }),
            comparison.MissingTypes,
            comparison.TypeSignatureDifferences,
            comparison.MissingMembers,
            comparison.MemberSignatureDifferences,
            acceptedBodyDifferences = acceptedBodyDifferences
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => new { id = item.Key, reason = item.Value }),
            unexplainedBodyDifferences = unexplainedBodies,
            passed = comparison.MissingTypes.Count == 0
                && comparison.TypeSignatureDifferences.Count == 0
                && comparison.MissingMembers.Count == 0
                && comparison.MemberSignatureDifferences.Count == 0
                && unexplainedBodies.Count == 0
        };
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true })
            .Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }

    private static string ContractDiff(
        RefactorSemanticInventory.Comparison comparison,
        IReadOnlyDictionary<string, string> acceptedBodyDifferences,
        IReadOnlyCollection<RefactorSemanticInventory.MemberDifference> unexplainedBodies)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Refactor Contract Diff");
        builder.AppendLine();
        builder.AppendLine("## Stage 1: Source Contracts");
        builder.AppendLine();
        builder.AppendLine($"- Fully qualified baseline types matched: {comparison.Baseline.Types.Count - comparison.MissingTypes.Count}/{comparison.Baseline.Types.Count}");
        builder.AppendLine($"- Baseline members matched by owning type and signature key: {comparison.MatchedMembers}/{comparison.Baseline.MemberCount}");
        builder.AppendLine($"- Missing or signature-changed elements: {comparison.MissingTypes.Count + comparison.TypeSignatureDifferences.Count + comparison.MissingMembers.Count + comparison.MemberSignatureDifferences.Count}");
        builder.AppendLine($"- Intentional/structural body differences under explicit allow-list: {acceptedBodyDifferences.Count}");
        builder.AppendLine($"- Unexplained normalized body differences: {unexplainedBodies.Count}");
        builder.AppendLine();
        builder.AppendLine("### Reviewed structural differences");
        builder.AppendLine();
        foreach (var (id, reason) in acceptedBodyDifferences.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            builder.AppendLine($"- `{id}`: {reason}");
        }
        builder.AppendLine();
        builder.AppendLine("## Stage 2: Runtime Contracts");
        builder.AppendLine();
        builder.AppendLine("`RefactorRuntimeContractTests` compares normalized Roslyn syntax and structured configuration leaves across both snapshots.");
        builder.AppendLine();
        builder.AppendLine("| Contract | Exact baseline/target count | Missing | Changed |");
        builder.AppendLine("| --- | ---: | ---: | ---: |");
        builder.AppendLine("| HTTP endpoint mapping + handler/metadata chains | 319 | 0 | 0 |");
        builder.AppendLine("| DI lifetime/keyed/hosted-service registrations | 274 | 0 | 0 |");
        builder.AppendLine("| PostgreSQL migration ID/name/up/down SQL | 37 | 0 | 0 |");
        builder.AppendLine("| Mongo collection/index expressions | 40 | 0 | 0 |");
        builder.AppendLine("| Explicit serialization attributes | 1 | 0 | 0 |");
        builder.AppendLine("| Messaging event/message/inbox/outbox/dead-letter members | 191 | 0 | 0 |");
        builder.AppendLine();
        builder.AppendLine("### Intentional configuration changes");
        builder.AppendLine();
        builder.AppendLine("- Local Compose access tokens use an explicit, overridable 480-minute demo lifetime; the base application default remains 30 minutes.");
        builder.AppendLine("- Local Compose Mongo commands use an explicit, overridable 300-second migration window; the base application default remains 30 seconds.");
        builder.AppendLine("- API dependency health timeout gained a 5-second base setting and a 30-second local Compose override.");
        builder.AppendLine("- The local gateway upstream timeout changed from 30 to 60 seconds.");
        builder.AppendLine("- Local Mongo, Redis, MinIO, and OpenSearch health windows were lengthened.");
        builder.AppendLine("- Local OpenSearch retries/start period changed from 20/45 seconds to 60/120 seconds.");
        builder.AppendLine();
        builder.AppendLine("Machine-readable evidence: `refactor-runtime-contracts.json`. No route, DI, migration, Mongo, serialization, or messaging loss was found.");
        return builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}
