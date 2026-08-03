using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Runtime;
using Zumbo.BuildingBlocks.Application.Search;

namespace Zumbo.BuildingBlocks.Infrastructure.Search;

public sealed class InMemoryWorkItemSearchIndex : IWorkItemSearchIndex
{
    private readonly ConcurrentDictionary<string, WorkItemSearchRecord> _records = new();

    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task IndexAsync(WorkItemSearchRecord record, CancellationToken cancellationToken = default)
    {
        ValidateScope(record.OrganizationId, record.ProjectId);
        _records[record.Id] = record;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        _records.TryRemove(id, out _);
        return Task.CompletedTask;
    }

    public Task<WorkItemSearchResult> SearchAsync(WorkItemSearchQuery query, CancellationToken cancellationToken = default)
    {
        ValidateScope(query.OrganizationId, query.ProjectId);
        var text = query.Text?.Trim().ToLowerInvariant();
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var matches = _records.Values
            .Where(x => x.OrganizationId == query.OrganizationId && x.ProjectId == query.ProjectId)
            .Where(x => string.IsNullOrEmpty(query.AssigneeUserId) || x.AssigneeUserId == query.AssigneeUserId)
            .Where(x => string.IsNullOrEmpty(query.Status) || x.Status == query.Status)
            .Where(x => string.IsNullOrEmpty(query.IssueType) || x.Type == query.IssueType)
            .Where(x => string.IsNullOrEmpty(query.CustomFieldKey)
                || (x.CustomFieldExactValues ?? []).Contains(
                    ExactCustomFieldValue(query.CustomFieldKey, query.CustomFieldValue ?? string.Empty),
                    StringComparer.Ordinal))
            .Where(x => string.IsNullOrEmpty(text)
                || x.Title.Contains(text, StringComparison.OrdinalIgnoreCase)
                || x.Description.Contains(text, StringComparison.OrdinalIgnoreCase)
                || x.CustomFieldSearchText.Contains(text, StringComparison.OrdinalIgnoreCase)
                || x.Labels.Any(label => label.Contains(text, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(x => x.Title, StringComparer.Ordinal)
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .ToList();
        var ids = matches
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => x.Id)
            .ToList();

        return Task.FromResult(new WorkItemSearchResult(ids, matches.Count));
    }

    public Task<WorkItemSearchRebuildResult> RebuildAsync(
        IReadOnlyCollection<WorkItemSearchRecord> records,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateRebuildRecords(records, int.MaxValue);
        var replacement = records.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var removed = _records.Keys.Count(id => !replacement.ContainsKey(id));
        _records.Clear();
        foreach (var record in replacement.Values) _records[record.Id] = record;
        return Task.FromResult(new WorkItemSearchRebuildResult("in-memory-v1", replacement.Count, removed, true));
    }

    internal static void ValidateRebuildRecords(IReadOnlyCollection<WorkItemSearchRecord> records, int maximum)
    {
        if (records.Count > maximum)
            throw new InvalidOperationException($"Search rebuild exceeds the configured limit of {maximum} records.");
        if (records.Any(x => string.IsNullOrWhiteSpace(x.OrganizationId) || string.IsNullOrWhiteSpace(x.ProjectId)))
            throw new InvalidOperationException("Search rebuild records require organization and project scope.");
        if (records.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != records.Count)
            throw new InvalidOperationException("Search rebuild records must have unique ids.");
    }

    private static void ValidateScope(string organizationId, string projectId)
    {
        if (string.IsNullOrWhiteSpace(organizationId) || string.IsNullOrWhiteSpace(projectId))
            throw new ArgumentException("Search queries require organization and project scope.");
    }

    private static string ExactCustomFieldValue(string key, string value) =>
        $"{key.Trim()}\u001f{value.Trim()}";
}
