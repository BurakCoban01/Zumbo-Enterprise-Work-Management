using System.Text.RegularExpressions;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

internal static partial class DevelopmentWebhookReferencePolicy
{
    public static IReadOnlyCollection<DevelopmentWorkItemReference> ExtractWithinLimit(
        IEnumerable<string>? referenceTexts)
    {
        var references = (referenceTexts ?? [])
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .SelectMany(text => WorkItemReferencePattern().Matches(text)
                .Select(match => new DevelopmentWorkItemReference(
                    match.Groups["project"].Value.ToUpperInvariant(),
                    match.Groups["id"].Value.ToLowerInvariant())))
            .Distinct()
            .ToList();
        if (references.Count > DevelopmentIntegrationLimits.MaximumWorkItemReferencesPerEvent)
            throw new DevelopmentWebhookReferenceLimitException();
        return references;
    }

    [GeneratedRegex(
        @"(?<![A-Z0-9])(?<project>[A-Z][A-Z0-9]{1,15})-(?<id>[0-9A-F]{8})(?![0-9A-F])",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex WorkItemReferencePattern();
}
