using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

internal static class WorkflowIssueTypeSchemes
{
    public static IReadOnlyCollection<WorkflowIssueTypeSchemeRequest> Normalize(
        IReadOnlyCollection<WorkflowIssueTypeSchemeRequest>? requested,
        IReadOnlyCollection<WorkflowStatusRequest> statuses,
        IReadOnlyCollection<WorkflowTransitionRequest> transitions)
    {
        var statusByName = statuses.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
        var source = requested is { Count: > 0 }
            ? requested
            :
            [
                new WorkflowIssueTypeSchemeRequest(
                    "*",
                    statuses.First(x => x.Category == "Todo").Name,
                    statuses.Select(x => x.Name).ToArray(),
                    statuses.Where(x => x.Category == "Done").Select(x => x.Name).ToArray())
            ];
        if (source.Count > 20)
        {
            throw new ValidationException("A workflow cannot contain more than 20 issue type schemes.");
        }

        var normalized = source.Select(scheme => NormalizeOne(scheme, statusByName, transitions)).ToArray();
        if (normalized.GroupBy(x => x.IssueType, StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1))
        {
            throw new ConflictException("WORKFLOW_ISSUE_SCHEME_DUPLICATE", "Issue type schemes must be unique.");
        }

        return normalized;
    }

    private static WorkflowIssueTypeSchemeRequest NormalizeOne(
        WorkflowIssueTypeSchemeRequest scheme,
        IReadOnlyDictionary<string, WorkflowStatusRequest> statusByName,
        IReadOnlyCollection<WorkflowTransitionRequest> transitions)
    {
        var issueType = scheme.IssueType?.Trim() ?? string.Empty;
        if (issueType.Length is < 1 or > 40)
        {
            throw new ValidationException("Workflow issue type must contain 1-40 characters.");
        }

        var allowed = (scheme.Statuses ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (allowed.Length == 0 || allowed.Any(x => !statusByName.ContainsKey(x)))
        {
            throw new ConflictException("WORKFLOW_ISSUE_SCHEME_STATUS_INVALID", "Every issue scheme status must exist in the workflow.");
        }

        var defaultStatus = scheme.DefaultStatus?.Trim() ?? string.Empty;
        if (!allowed.Contains(defaultStatus, StringComparer.OrdinalIgnoreCase)
            || statusByName[defaultStatus].Category != "Todo")
        {
            throw new ConflictException("WORKFLOW_ISSUE_SCHEME_DEFAULT_INVALID", "Each issue scheme requires one Todo default status.");
        }

        var done = (scheme.DoneStatuses is { Count: > 0 }
                ? scheme.DoneStatuses
                : allowed.Where(x => statusByName[x].Category == "Done"))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (done.Length == 0 || done.Any(x =>
                !allowed.Contains(x, StringComparer.OrdinalIgnoreCase)
                || statusByName[x].Category != "Done"))
        {
            throw new ConflictException("WORKFLOW_ISSUE_SCHEME_DONE_INVALID", "Each issue scheme requires at least one Done status.");
        }

        ValidateReachability(defaultStatus, allowed, done, transitions);
        return new WorkflowIssueTypeSchemeRequest(issueType, defaultStatus, allowed, done);
    }

    private static void ValidateReachability(
        string defaultStatus,
        IReadOnlyCollection<string> allowed,
        IReadOnlyCollection<string> done,
        IReadOnlyCollection<WorkflowTransitionRequest> transitions)
    {
        var allowedSet = allowed.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var adjacency = transitions
            .Where(x => allowedSet.Contains(x.FromStatus) && allowedSet.Contains(x.ToStatus))
            .GroupBy(x => x.FromStatus, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Select(edge => edge.ToStatus).ToList(), StringComparer.OrdinalIgnoreCase);
        if (!Traverse([defaultStatus], adjacency).IsSupersetOf(allowedSet))
        {
            throw new ConflictException("WORKFLOW_ISSUE_SCHEME_UNREACHABLE", "Every issue scheme status must be reachable from its default status.");
        }

        var doneSet = done.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (allowedSet.Where(x => !doneSet.Contains(x)).Any(x => !Traverse([x], adjacency).Overlaps(doneSet)))
        {
            throw new ConflictException("WORKFLOW_ISSUE_SCHEME_DONE_UNREACHABLE", "Every non-done issue scheme status must reach a Done status.");
        }
    }

    private static HashSet<string> Traverse(
        IEnumerable<string> starts,
        IReadOnlyDictionary<string, List<string>> adjacency)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>(starts);
        while (pending.TryPop(out var current))
        {
            if (!visited.Add(current)) continue;
            if (adjacency.TryGetValue(current, out var targets))
            {
                foreach (var target in targets) pending.Push(target);
            }
        }

        return visited;
    }
}
