using System.Text.Json;
using Microsoft.Extensions.Options;
using Zumbo.Modules.WorkItems;

namespace Zumbo.ApiTests;

public sealed class WorkItemRealtimeContractTests
{
    [Fact]
    public void EventEnvelopeIsVersionedBoundedAndCarriesResourceVersion()
    {
        var item = new WorkItemRealtimeItem(
            "work-item-1", "project-1", "board-1", "column-1", "Realtime item",
            "Task", "High", "In Progress", "user-1", null, null, 3, null, 1_000_000, 7);
        var change = new WorkItemRealtimeChange(
            "updated", item.Id, item.ProjectId, item.BoardId, item,
            "correlation-1", DateTimeOffset.UtcNow,
            WorkItemRealtimeProtocol.CurrentSchemaVersion, item.Version);

        Assert.Equal(1, change.SchemaVersion);
        Assert.Equal(7, change.ResourceVersion);
        Assert.True(JsonSerializer.SerializeToUtf8Bytes(change).Length < 2048);
    }

    [Fact]
    public void RealtimeLimitsAreBoundedAndRejectInvalidConfigurations()
    {
        var valid = new WorkItemRealtimeOptions();
        valid.Validate();
        Assert.InRange(valid.MaximumProjectSubscriptionsPerConnection, 1, 32);
        Assert.InRange(valid.TransportMaxBufferBytes, valid.ApplicationMaxBufferBytes, 1_048_576);
        Assert.InRange(valid.StatefulReconnectBufferBytes, 4096, 1_048_576);

        Assert.Throws<InvalidOperationException>(() => new WorkItemRealtimeOptions
        {
            MaximumProjectSubscriptionsPerConnection = 0
        }.Validate());
        Assert.Throws<InvalidOperationException>(() => new WorkItemRealtimeOptions
        {
            ApplicationMaxBufferBytes = 65_536,
            TransportMaxBufferBytes = 4096
        }.Validate());
    }

    [Fact]
    public void ProjectGroupsAreOpaqueDeterministicAndTenantInputCannotCollide()
    {
        var first = WorkItemHub.ProjectGroup("project-1");
        Assert.Equal(first, WorkItemHub.ProjectGroup("project-1"));
        Assert.NotEqual(first, WorkItemHub.ProjectGroup("project:1"));
        Assert.DoesNotContain("project-1", first, StringComparison.Ordinal);
        Assert.Equal(72, first.Length);
    }

    [Fact]
    public async Task PublisherRejectsOversizedEventsBeforeSendingToAClient()
    {
        var item = new WorkItemRealtimeItem(
            "work-item-1", "project-1", "board-1", "column-1", new string('x', 4096),
            "Task", "High", "In Progress", "user-1", null, null, 3, null, 1_000_000, 7);
        var change = new WorkItemRealtimeChange(
            "updated", item.Id, item.ProjectId, item.BoardId, item,
            "correlation-1", DateTimeOffset.UtcNow,
            WorkItemRealtimeProtocol.CurrentSchemaVersion, item.Version);
        var publisher = new SignalRWorkItemRealtimePublisher(null!, Options.Create(new WorkItemRealtimeOptions
        {
            MaximumPayloadBytes = 2048
        }));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => publisher.PublishAsync(change, CancellationToken.None));

        Assert.Contains("payload limit", error.Message, StringComparison.Ordinal);
    }
}
