using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Zumbo.Capacity;

internal static class CapacityStorageProbe
{
    private static readonly string[] MongoDatabases =
    [
        "ZumboAudit",
        "ZumboBoards",
        "ZumboIdentity",
        "ZumboNotifications",
        "ZumboOrganizations",
        "ZumboProjects",
        "ZumboWorkItems"
    ];

    public static async Task<long> MeasureAsync(
        string mongoConnectionString,
        string openSearchBaseUrl,
        CancellationToken ct)
    {
        var mongo = new MongoClient(mongoConnectionString);
        long total = 0;
        foreach (var databaseName in MongoDatabases)
        {
            var stats = await mongo.GetDatabase(databaseName).RunCommandAsync<BsonDocument>(
                new BsonDocument("dbStats", 1),
                cancellationToken: ct);
            total += Numeric(stats, "dataSize") + Numeric(stats, "indexSize");
        }

        using var search = new HttpClient
        {
            BaseAddress = new Uri(openSearchBaseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(30)
        };
        using var response = await search.GetAsync("zumbo-work-items/_stats/store", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return total;
        }
        response.EnsureSuccessStatusCode();
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        var storeBytes = payload.RootElement
            .GetProperty("_all")
            .GetProperty("primaries")
            .GetProperty("store")
            .GetProperty("size_in_bytes")
            .GetInt64();
        return checked(total + storeBytes);
    }

    private static long Numeric(BsonDocument document, string name) =>
        document.TryGetValue(name, out var value) && value.IsNumeric ? value.ToInt64() : 0;
}

