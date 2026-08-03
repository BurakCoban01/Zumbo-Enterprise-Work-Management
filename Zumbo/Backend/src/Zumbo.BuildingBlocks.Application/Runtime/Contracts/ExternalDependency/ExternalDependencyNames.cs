namespace Zumbo.BuildingBlocks.Application.Runtime;

public static class ExternalDependencyNames
{
    public const string MongoDb = "mongodb";
    public const string PostgreSql = "postgresql";
    public const string Redis = "redis";
    public const string Minio = "minio";
    public const string OpenSearch = "opensearch";
    public const string Smtp = "smtp";
    public const string Webhook = "webhook";

    public static IReadOnlyList<string> All { get; } =
    [
        MongoDb,
        PostgreSql,
        Redis,
        Minio,
        OpenSearch,
        Smtp,
        Webhook
    ];
}
