using System.Text.Json;
using Zumbo.Modules.Teams;
using Zumbo.Modules.WorkItems;

namespace Zumbo.UnitTests;

public sealed class NotificationExtractionContractTests
{
    [Fact]
    public void ProducerOwnedNotificationEvents_PreserveExactV1WireContracts()
    {
        var workItem = new WorkItemNotificationEvent("user-1", "Mention", "message", "dedupe-1");
        var team = new TeamInvitationNotificationEvent(
            "user-1",
            "team-1",
            "invite-1",
            "Core Team",
            "user-2",
            "dedupe-2");

        Assert.Equal("work-item.notification.v1", WorkItemDurableEventTypes.Notification);
        Assert.Equal("team.invitation-notification.v1", TeamDurableEventTypes.InvitationNotification);
        Assert.Equal(
            ["DeduplicationKey", "Message", "Type", "UserId"],
            PropertyNames(JsonSerializer.Serialize(workItem)));
        Assert.Equal(
            ["DeduplicationKey", "InviteId", "InvitedByUserId", "TeamId", "TeamName", "UserId"],
            PropertyNames(JsonSerializer.Serialize(team)));

        Assert.Equal(workItem, JsonSerializer.Deserialize<WorkItemNotificationEvent>(JsonSerializer.Serialize(workItem)));
        Assert.Equal(team, JsonSerializer.Deserialize<TeamInvitationNotificationEvent>(JsonSerializer.Serialize(team)));
    }

    private static IReadOnlyList<string> PropertyNames(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }
}
