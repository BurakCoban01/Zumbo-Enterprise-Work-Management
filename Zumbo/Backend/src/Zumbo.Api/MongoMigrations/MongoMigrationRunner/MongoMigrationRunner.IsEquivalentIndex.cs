using MongoDB.Bson;

public sealed partial class MongoMigrationRunner
{
    private static bool IsEquivalentIndex(
        BsonDocument current,
        MongoIndexSpecification expected)
    {
        if (current["key"].AsBsonDocument != expected.Keys
            || current.GetValue("unique", false).ToBoolean() != expected.Unique)
        {
            return false;
        }

        var currentCaseInsensitive = current.TryGetValue("collation", out var collation)
            && collation.AsBsonDocument.GetValue("locale", string.Empty).AsString == "en"
            && collation.AsBsonDocument.GetValue("strength", 0).ToInt32() == 2;
        if (currentCaseInsensitive != expected.CaseInsensitive)
        {
            return false;
        }

        var currentExpiry = current.TryGetValue("expireAfterSeconds", out var expiry)
            ? TimeSpan.FromSeconds(expiry.ToInt64())
            : (TimeSpan?)null;
        if (currentExpiry != expected.ExpireAfter)
        {
            return false;
        }

        var currentPartialFilter = current.TryGetValue("partialFilterExpression", out var partial)
            ? partial.AsBsonDocument
            : null;
        return currentPartialFilter == expected.PartialFilter;
    }
}
