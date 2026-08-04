using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems.Application.Features.Development.Webhooks;

public sealed class ApplyWebhookLinksHandler(
    IDocumentRepository<WorkItemDevelopmentLinkDocument> links,
    IDocumentRepository<WorkItemDocument> workItems,
    IClock clock)
{
    public async Task<int> HandleAsync(
        DevelopmentConnectionDocument connection,
        DevelopmentRepositoryMappingDocument mapping,
        NormalizedDevelopmentEvent providerEvent,
        string deliveryId,
        CancellationToken ct)
    {
        _ = deliveryId;
        var candidates = await ListAllAsync(
            links,
            item => item.OrganizationId == connection.OrganizationId
                && item.MappingId == mapping.Id
                && (item.ExternalId == providerEvent.ExternalId
                    || providerEvent.CommitSha != null
                        && item.CommitSha == providerEvent.CommitSha),
            ct);
        var createdLinkIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var workItem in await ResolveReferencedWorkItemsAsync(
                     mapping,
                     providerEvent,
                     ct))
        {
            var id = StableId(
                connection.Id,
                mapping.Id,
                workItem.Id,
                providerEvent.Kind,
                providerEvent.ExternalId);
            if (candidates.All(item => item.Id != id))
            {
                var existing = await links.SelectAsync(
                    item => item.Id == id && item.OrganizationId == connection.OrganizationId,
                    ct);
                if (existing is not null)
                {
                    candidates.Add(existing);
                }
                else if (await links.CountByFilterAsync(
                    item => item.OrganizationId == connection.OrganizationId
                        && item.WorkItemId == workItem.Id,
                    ct) < DevelopmentIntegrationLimits.MaximumLinksPerWorkItem)
                {
                    var now = clock.UtcNow;
                    var created = await links.CreateAsync(new WorkItemDevelopmentLinkDocument
                    {
                        Id = id,
                        OrganizationId = connection.OrganizationId,
                        ConnectionId = connection.Id,
                        MappingId = mapping.Id,
                        ProjectId = mapping.ProjectId,
                        WorkItemId = workItem.Id,
                        Provider = connection.Provider,
                        RepositoryFullName = mapping.RepositoryFullName,
                        Kind = providerEvent.Kind,
                        ExternalId = providerEvent.ExternalId,
                        Title = providerEvent.Title,
                        Url = providerEvent.Url,
                        Branch = providerEvent.Branch,
                        CommitSha = providerEvent.CommitSha,
                        Status = providerEvent.Status,
                        Source = "Webhook",
                        LastEventAtUtc = providerEvent.OccurredAtUtc ?? now,
                        CreatedAtUtc = now,
                        UpdatedAtUtc = now
                    }, ct);
                    candidates.Add(created);
                    createdLinkIds.Add(created.Id);
                }
            }
        }

        var applied = 0;
        foreach (var link in candidates.DistinctBy(item => item.Id))
        {
            if (createdLinkIds.Contains(link.Id))
            {
                applied++;
                continue;
            }

            var eventTime = providerEvent.OccurredAtUtc ?? clock.UtcNow;
            if (link.LastEventAtUtc is not null && link.LastEventAtUtc > eventTime)
            {
                continue;
            }

            var before = $"{link.Status}|{link.Title}|{link.Url}|{link.Branch}|{link.CommitSha}";
            link.Title = providerEvent.Title;
            link.Url = providerEvent.Url;
            link.Branch = providerEvent.Branch ?? link.Branch;
            link.CommitSha = providerEvent.CommitSha ?? link.CommitSha;
            link.Status = providerEvent.Status;
            link.Source = "Webhook";
            link.LastEventAtUtc = eventTime;
            link.UpdatedAtUtc = clock.UtcNow;
            var after = $"{link.Status}|{link.Title}|{link.Url}|{link.Branch}|{link.CommitSha}";
            if (before == after && link.Version > 0)
            {
                continue;
            }

            try
            {
                var result = await links.ReplaceByVersionAsync(
                    item => item.Id == link.Id
                        && item.OrganizationId == link.OrganizationId,
                    link,
                    link.Version,
                    ct);
                if (result.Found)
                {
                    link.Version = result.Version!.Value;
                    applied++;
                }
            }
            catch (DocumentConcurrencyException)
            {
                var current = await links.SelectAsync(
                    item => item.Id == link.Id
                        && item.OrganizationId == link.OrganizationId,
                    ct);
                if (current is not null
                    && current.Status == providerEvent.Status
                    && current.Url == providerEvent.Url)
                {
                    continue;
                }

                throw new ConflictException(
                    "DEVELOPMENT_LINK_CONFLICT",
                    "Development link changed concurrently; retry the webhook delivery.");
            }
        }

        return applied;
    }

    public static NormalizedDevelopmentEvent NormalizeProviderEvent(
        DevelopmentRepositoryMappingDocument mapping,
        NormalizedDevelopmentEvent source) =>
        source with
        {
            Url = NormalizeLinkUrl(mapping.RepositoryUrl, source.Url),
            Status = NormalizeStatus(source.Status)
        };

    private async Task<IReadOnlyCollection<WorkItemDocument>> ResolveReferencedWorkItemsAsync(
        DevelopmentRepositoryMappingDocument mapping,
        NormalizedDevelopmentEvent providerEvent,
        CancellationToken ct)
    {
        var prefixes = DevelopmentWebhookReferencePolicy
            .ExtractWithinLimit(providerEvent.ReferenceTexts)
            .Where(reference => string.Equals(
                reference.ProjectKey,
                mapping.ProjectKey,
                StringComparison.OrdinalIgnoreCase))
            .Select(reference => reference.WorkItemIdPrefix)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var result = new List<WorkItemDocument>();
        foreach (var prefix in prefixes)
        {
            var matches = await workItems.ListByFilterAsync(
                item => item.ProjectId == mapping.ProjectId
                    && item.Id.StartsWith(prefix)
                    && !item.Archived,
                item => item.Id,
                pageSize: 2,
                cancellationToken: ct);
            if (matches.Count == 1)
            {
                result.Add(matches[0]);
            }
        }

        return result;
    }

    private static async Task<List<TDocument>> ListAllAsync<TDocument>(
        IDocumentRepository<TDocument> repository,
        Expression<Func<TDocument, bool>> filter,
        CancellationToken ct)
        where TDocument : class, IDocument
    {
        var result = new List<TDocument>();
        string? cursor = null;
        do
        {
            var page = await repository.ListByCursorAsync(filter, cursor, 200, ct);
            result.AddRange(page.Items);
            cursor = page.NextCursor;
        }
        while (cursor is not null);
        return result;
    }

    private static string NormalizeLinkUrl(string repositoryUrl, string value)
    {
        var normalized = NormalizeHttpsUrl(value, "Development link URL");
        if (!new Uri(normalized).Host.Equals(
                new Uri(repositoryUrl).Host,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException(
                "Development link URL host must match the mapped repository.");
        }

        return normalized;
    }

    private static string NormalizeHttpsUrl(string value, string label)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > 2_048
            || !Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrWhiteSpace(uri.UserInfo)
            || !string.IsNullOrWhiteSpace(uri.Fragment)
            || string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new ValidationException($"{label} must be a safe absolute HTTPS URL.");
        }

        return uri.AbsoluteUri;
    }

    private static string NormalizeStatus(string value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "open" => "Open",
            "merged" => "Merged",
            "closed" => "Closed",
            "success" => "Success",
            "failed" => "Failed",
            "pending" => "Pending",
            "running" => "Running",
            "pushed" => "Pushed",
            "unknown" or "" or null => "Unknown",
            _ => throw new ValidationException("Development status is not supported.")
        };

    private static string StableId(params string[] values) =>
        Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(string.Join('\u001f', values))))
            .ToLowerInvariant()[..32];
}
