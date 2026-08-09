using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects.Application.Features.Portfolio;

internal static class PortfolioValidation
{
    internal const int MaximumInitiatives = 100;
    internal const int MaximumDependencies = 200;
    private const int MaximumProjectsPerInitiative = 20;
    private const int MaximumHierarchyDepth = 5;

    internal static List<string> NormalizeIds(
        IReadOnlyCollection<string>? values,
        int maximum,
        string label)
    {
        var normalized = (values ?? throw new ValidationException($"{label} list is required."))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (normalized.Count > maximum || normalized.Any(value => value.Length > 128))
            throw new ValidationException($"{label} list is outside the supported bounds.");
        return normalized;
    }

    internal static string Required(string? value, string label, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ValidationException($"{label} is required.");
        var normalized = value.Trim();
        if (normalized.Length > maximum)
            throw new ValidationException($"{label} cannot exceed {maximum} characters.");
        return normalized;
    }

    internal static string? Optional(string? value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        if (normalized.Length > maximum)
            throw new ValidationException($"Value cannot exceed {maximum} characters.");
        return normalized;
    }

    internal static string Allowed(
        string? value,
        IReadOnlySet<string> allowed,
        string label)
    {
        var normalized = Required(value, label, 32);
        return allowed.Contains(normalized)
            ? normalized
            : throw new ValidationException($"{label} is not supported.");
    }

    internal static int? Confidence(int? value)
    {
        if (value is < 0 or > 100)
        {
            throw new ValidationException(
                "Initiative confidence must be between 0 and 100.");
        }
        return value;
    }

    internal static SaveInitiativeRequest Normalize(SaveInitiativeRequest request)
    {
        var projectIds = NormalizeIds(
            request.ProjectIds,
            MaximumProjectsPerInitiative,
            "Initiative project");
        var links = (request.MilestoneLinks
                ?? throw new ValidationException("Initiative milestone links are required."))
            .Select(link => new PortfolioMilestoneLinkRequest(
                Required(link.ProjectId, "Milestone project", 128),
                Required(link.MilestoneId, "Milestone", 128)))
            .Distinct()
            .ToList();
        if (links.Count > 50)
            throw new ValidationException("An initiative cannot contain more than 50 milestone links.");
        if (links.Any(link => !projectIds.Contains(link.ProjectId)))
            throw new ValidationException("Milestone links must belong to an initiative project.");
        return request with
        {
            Name = Required(request.Name, "Initiative name", 120),
            Summary = Optional(request.Summary, 1000),
            ParentInitiativeId = Optional(request.ParentInitiativeId, 128),
            OwnerUserId = Required(request.OwnerUserId, "Initiative owner", 128),
            Status = Allowed(request.Status, InitiativeStatuses.Allowed, "Initiative status"),
            Health = Allowed(request.Health, InitiativeHealth.Allowed, "Initiative health"),
            Confidence = Confidence(request.Confidence),
            ProjectIds = projectIds,
            MilestoneLinks = links
        };
    }

    internal static void ValidateHierarchy(
        IReadOnlyCollection<InitiativeDocument> initiatives)
    {
        var byId = initiatives.ToDictionary(item => item.Id, StringComparer.Ordinal);
        foreach (var initiative in initiatives)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal) { initiative.Id };
            var current = initiative;
            var depth = 1;
            while (current.ParentInitiativeId is not null)
            {
                if (!byId.TryGetValue(current.ParentInitiativeId, out current!))
                {
                    throw new ValidationException(
                        "Parent initiative must belong to the same portfolio.");
                }
                if (!seen.Add(current.Id))
                    throw new ValidationException("Initiative hierarchy cannot contain cycles.");
                depth++;
                if (depth > MaximumHierarchyDepth)
                {
                    throw new ValidationException(
                        $"Initiative hierarchy cannot exceed {MaximumHierarchyDepth} levels.");
                }
            }
        }
    }

    internal static void ValidateDependencyGraph(
        IReadOnlyCollection<PortfolioProjectDependencyDocument> dependencies)
    {
        var active = dependencies
            .Where(item => item.Status == PortfolioDependencyStatuses.Active)
            .ToList();
        if (active.GroupBy(
                item => $"{item.SourceProjectId}\u001f{item.TargetProjectId}",
                StringComparer.Ordinal).Any(group => group.Count() > 1))
        {
            throw new ValidationException("Active portfolio dependencies must be unique.");
        }
        var targets = active
            .GroupBy(item => item.SourceProjectId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.TargetProjectId).ToList(),
                StringComparer.Ordinal);
        foreach (var start in targets.Keys)
        {
            var visiting = new HashSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            if (HasCycle(start, targets, visiting, visited))
            {
                throw new ValidationException(
                    "Active portfolio dependencies cannot contain cycles.");
            }
        }
    }

    private static bool HasCycle(
        string node,
        IReadOnlyDictionary<string, List<string>> targets,
        ISet<string> visiting,
        ISet<string> visited)
    {
        if (visited.Contains(node)) return false;
        if (!visiting.Add(node)) return true;
        if (targets.TryGetValue(node, out var next)
            && next.Any(target => HasCycle(target, targets, visiting, visited)))
        {
            return true;
        }
        visiting.Remove(node);
        visited.Add(node);
        return false;
    }
}
