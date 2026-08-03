using MongoDB.Driver;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

namespace Zumbo.BuildingBlocks.Infrastructure.Messaging;

public sealed class MongoInboxDocument
{
    public string Id { get; set; } = string.Empty;
    public string ConsumerName { get; set; } = string.Empty;
    public string MessageId { get; set; } = string.Empty;
    public DateTime ProcessedAtUtc { get; set; }

    public static string Key(string consumerName, string messageId)
    {
        if (string.IsNullOrWhiteSpace(consumerName) || string.IsNullOrWhiteSpace(messageId))
        {
            throw new ArgumentException("Inbox consumer and message identifiers are required.");
        }

        return $"{consumerName.Trim()}:{messageId.Trim()}";
    }
}
