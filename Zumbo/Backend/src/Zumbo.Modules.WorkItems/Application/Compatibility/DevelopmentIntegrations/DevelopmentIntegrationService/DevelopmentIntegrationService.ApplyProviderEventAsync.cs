using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class DevelopmentIntegrationService{

    private async Task<int> ApplyProviderEventAsync(
        DevelopmentConnectionDocument connection,
        DevelopmentRepositoryMappingDocument mapping,
        NormalizedDevelopmentEvent providerEvent,
        string deliveryId,
        CancellationToken ct)
    {
        var candidates = await ListAllAsync(
            links,
            item => item.OrganizationId == connection.OrganizationId
                && item.MappingId == mapping.Id
                && (item.ExternalId == providerEvent.ExternalId
                    || providerEvent.CommitSha != null
                        && item.CommitSha == providerEvent.CommitSha),
            ct);
        var createdLinkIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var workItem in await ResolveReferencedWorkItemsAsync(mapping, providerEvent, ct))
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
                continue;
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
            if (before == after && link.Version > 0) continue;
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

}
