using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Npgsql;
using NpgsqlTypes;
using Zumbo.BuildingBlocks.Application.Runtime;

namespace Zumbo.DataTransfer;

internal sealed record ProviderTarget(string Provider, string ConnectionString, string? MongoDatabase);
internal sealed record TransferSnapshot(string Name, long Count, string Sha256);

internal static class TransferEngine
{
    internal static async Task<TransferManifest> ExportAsync(
        ProviderTarget source,
        string bundlePath,
        CancellationToken ct)
    {
        await EnsureTransportQuiescedAsync(source, ct);
        var root = PrepareNewBundle(bundlePath);
        var results = new List<TransferManifestDataset>();
        foreach (var dataset in TransferCatalog.All)
        {
            var fileName = dataset.Name.Replace('.', '-') + ".ndjson";
            var filePath = Path.Combine(root, fileName);
            await using var stream = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, true);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\n" };
            long count = 0;
            await EnumerateAsync(source, dataset, async document =>
            {
                await writer.WriteLineAsync(TransferJson.SerializeCanonical(document, dataset.DocumentType));
                count++;
            }, ct);
            await writer.FlushAsync(ct);
            await stream.FlushAsync(ct);
            await writer.DisposeAsync();
            results.Add(new(dataset.Name, fileName, count, await Sha256Async(filePath, ct)));
        }

        var manifest = new TransferManifest(1, NormalizeProvider(source.Provider), DateTimeOffset.UtcNow, results);
        var manifestPath = Path.Combine(root, "manifest.json");
        await File.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false),
            ct);
        return manifest;
    }

    internal static async Task<IReadOnlyList<TransferSnapshot>> ValidateBundleAsync(
        string bundlePath,
        CancellationToken ct)
    {
        var (root, manifest) = await ReadManifestAsync(bundlePath, ct);
        if (manifest.SchemaVersion != 1) throw new InvalidDataException("Unsupported transfer manifest schema version.");
        var definitions = TransferCatalog.All.ToDictionary(x => x.Name, StringComparer.Ordinal);
        if (manifest.Datasets.Count != definitions.Count
            || manifest.Datasets.Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != definitions.Count)
        {
            throw new InvalidDataException("Transfer manifest dataset catalog is incomplete or duplicated.");
        }

        var snapshots = new List<TransferSnapshot>();
        foreach (var item in manifest.Datasets)
        {
            if (!definitions.TryGetValue(item.Name, out var definition))
                throw new InvalidDataException($"Unknown dataset '{item.Name}'.");
            var filePath = ResolveInside(root, item.File);
            if (!File.Exists(filePath)) throw new FileNotFoundException("Transfer dataset is missing.", filePath);
            var checksum = await Sha256Async(filePath, ct);
            if (!checksum.Equals(item.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Dataset '{item.Name}' checksum does not match the manifest.");
            long count = 0;
            await foreach (var line in File.ReadLinesAsync(filePath, ct))
            {
                if (string.IsNullOrWhiteSpace(line)) throw new InvalidDataException($"Dataset '{item.Name}' contains an empty row.");
                _ = TransferJson.Identity(TransferJson.Deserialize(line, definition.DocumentType));
                count++;
            }
            if (count != item.Count) throw new InvalidDataException($"Dataset '{item.Name}' count does not match the manifest.");
            snapshots.Add(new(item.Name, count, checksum));
        }
        return snapshots;
    }

    internal static async Task ImportAsync(
        ProviderTarget target,
        string bundlePath,
        bool dryRun,
        int? failAfter,
        CancellationToken ct)
    {
        await ValidateBundleAsync(bundlePath, ct);
        await EnsureTargetEmptyAsync(target, ct);
        if (dryRun) return;
        var (root, manifest) = await ReadManifestAsync(bundlePath, ct);
        if (NormalizeProvider(target.Provider) == "Mongo")
            await ImportMongoAsync(target, root, manifest, failAfter, ct);
        else
            await ImportPostgreSqlAsync(target, root, manifest, failAfter, ct);
    }

    internal static async Task VerifyAsync(ProviderTarget target, string bundlePath, CancellationToken ct)
    {
        var (_, manifest) = await ReadManifestAsync(bundlePath, ct);
        var expected = manifest.Datasets.ToDictionary(x => x.Name, StringComparer.Ordinal);
        foreach (var dataset in TransferCatalog.All)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long count = 0;
            await EnumerateAsync(target, dataset, document =>
            {
                var bytes = Encoding.UTF8.GetBytes(TransferJson.SerializeCanonical(document, dataset.DocumentType) + "\n");
                hash.AppendData(bytes);
                count++;
                return Task.CompletedTask;
            }, ct);
            var actualHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            var wanted = expected[dataset.Name];
            if (count != wanted.Count || !actualHash.Equals(wanted.Sha256, StringComparison.Ordinal))
                throw new InvalidDataException($"Target dataset '{dataset.Name}' count/checksum parity failed.");
        }
    }

    private static async Task EnumerateAsync(
        ProviderTarget source,
        TransferDataset dataset,
        Func<object, Task> consume,
        CancellationToken ct)
    {
        if (NormalizeProvider(source.Provider) == "Mongo")
        {
            var client = new MongoClient(source.ConnectionString);
            var database = client.GetDatabase(RequireMongoDatabase(source));
            var collection = database.GetCollection<BsonDocument>(dataset.MongoCollection);
            using var cursor = await collection.Find(FilterDefinition<BsonDocument>.Empty)
                .Sort(Builders<BsonDocument>.Sort.Ascending("_id"))
                .ToCursorAsync(ct);
            while (await cursor.MoveNextAsync(ct))
            {
                foreach (var bson in cursor.Current)
                    await consume(BsonSerializer.Deserialize(bson, dataset.DocumentType));
            }
            return;
        }

        await using var connection = new NpgsqlConnection(source.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT document::text FROM {Quote(dataset.PostgreSqlSchema)}.{Quote(dataset.PostgreSqlTable)} ORDER BY id;";
        await using var reader = await command.ExecuteReaderAsync(System.Data.CommandBehavior.SequentialAccess, ct);
        while (await reader.ReadAsync(ct))
            await consume(TransferJson.Deserialize(reader.GetString(0), dataset.DocumentType));
    }

    private static async Task EnsureTargetEmptyAsync(ProviderTarget target, CancellationToken ct)
    {
        if (NormalizeProvider(target.Provider) == "Mongo")
        {
            var db = new MongoClient(target.ConnectionString).GetDatabase(RequireMongoDatabase(target));
            foreach (var dataset in TransferCatalog.All)
            {
                if (await db.GetCollection<BsonDocument>(dataset.MongoCollection)
                    .CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty, cancellationToken: ct) != 0)
                    throw new InvalidOperationException($"Target dataset '{dataset.Name}' is not empty.");
            }
            return;
        }

        await using var connection = new NpgsqlConnection(target.ConnectionString);
        await connection.OpenAsync(ct);
        foreach (var dataset in TransferCatalog.All)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT count(*) FROM {Quote(dataset.PostgreSqlSchema)}.{Quote(dataset.PostgreSqlTable)};";
            if ((long)(await command.ExecuteScalarAsync(ct))! != 0)
                throw new InvalidOperationException($"Target dataset '{dataset.Name}' is not empty.");
        }
    }

    private static async Task EnsureTransportQuiescedAsync(ProviderTarget source, CancellationToken ct)
    {
        long active;
        if (NormalizeProvider(source.Provider) == "Mongo")
        {
            var collection = new MongoClient(source.ConnectionString)
                .GetDatabase(RequireMongoDatabase(source))
                .GetCollection<BsonDocument>("outbox_messages");
            active = await collection.CountDocumentsAsync(
                Builders<BsonDocument>.Filter.Ne("Status", "Completed"),
                cancellationToken: ct);
        }
        else
        {
            await using var connection = new NpgsqlConnection(source.ConnectionString);
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT count(*) FROM messaging.outbox_messages WHERE status <> 'Completed';";
            active = (long)(await command.ExecuteScalarAsync(ct))!;
        }

        if (active != 0)
        {
            throw new InvalidOperationException(
                $"Source has {active} non-completed durable outbox message(s); quiesce and reconcile transport state before export.");
        }
    }

    private static async Task ImportMongoAsync(
        ProviderTarget target,
        string root,
        TransferManifest manifest,
        int? failAfter,
        CancellationToken ct)
    {
        var client = new MongoClient(target.ConnectionString);
        var database = client.GetDatabase(RequireMongoDatabase(target));
        using var session = await client.StartSessionAsync(cancellationToken: ct);
        session.StartTransaction();
        var writes = 0;
        try
        {
            foreach (var dataset in TransferCatalog.All)
            {
                var item = manifest.Datasets.Single(x => x.Name == dataset.Name);
                await foreach (var line in File.ReadLinesAsync(ResolveInside(root, item.File), ct))
                {
                    var document = TransferJson.Deserialize(line, dataset.DocumentType);
                    await database.GetCollection<BsonDocument>(dataset.MongoCollection)
                        .InsertOneAsync(session, document.ToBsonDocument(dataset.DocumentType), cancellationToken: ct);
                    if (failAfter is not null && ++writes >= failAfter)
                        throw new InvalidOperationException("Requested import failure injection.");
                }
            }
            await session.CommitTransactionAsync(ct);
        }
        catch
        {
            if (session.IsInTransaction)
            {
                var cleanup = await CompensationExecution.RunAsync(
                    "data_transfer.mongo_import.abort",
                    token => session.AbortTransactionAsync(token));
                ObserveCompensation(cleanup);
            }
            throw;
        }
    }

    private static async Task ImportPostgreSqlAsync(
        ProviderTarget target,
        string root,
        TransferManifest manifest,
        int? failAfter,
        CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(target.ConnectionString);
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var writes = 0;
        try
        {
            foreach (var dataset in TransferCatalog.All)
            {
                var item = manifest.Datasets.Single(x => x.Name == dataset.Name);
                await foreach (var line in File.ReadLinesAsync(ResolveInside(root, item.File), ct))
                {
                    var document = TransferJson.Deserialize(line, dataset.DocumentType);
                    var identity = TransferJson.Identity(document);
                    await using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = $"INSERT INTO {Quote(dataset.PostgreSqlSchema)}.{Quote(dataset.PostgreSqlTable)} (id, version, document) VALUES (@id, @version, @document);";
                    command.Parameters.AddWithValue("id", identity.Id);
                    command.Parameters.AddWithValue("version", identity.Version);
                    command.Parameters.Add(new NpgsqlParameter("document", NpgsqlDbType.Jsonb) { Value = line });
                    await command.ExecuteNonQueryAsync(ct);
                    if (failAfter is not null && ++writes >= failAfter)
                        throw new InvalidOperationException("Requested import failure injection.");
                }
            }
            await transaction.CommitAsync(ct);
        }
        catch
        {
            var cleanup = await CompensationExecution.RunAsync(
                "data_transfer.postgres_import.rollback",
                token => transaction.RollbackAsync(token));
            ObserveCompensation(cleanup);
            throw;
        }
    }

    private static void ObserveCompensation(CompensationResult result)
    {
        if (!result.Succeeded)
        {
            Console.Error.WriteLine(
                $"Compensation operation {result.Operation} ended with {result.Outcome}; "
                + $"failure type {result.Exception?.GetType().Name ?? "none"}.");
        }
    }

    private static string PrepareNewBundle(string path)
    {
        var root = Path.GetFullPath(path);
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
            throw new InvalidOperationException("Transfer bundle directory must be new or empty.");
        Directory.CreateDirectory(root);
        return root;
    }

    private static async Task<(string Root, TransferManifest Manifest)> ReadManifestAsync(string path, CancellationToken ct)
    {
        var root = Path.GetFullPath(path);
        var manifestPath = Path.Combine(root, "manifest.json");
        var manifest = JsonSerializer.Deserialize<TransferManifest>(await File.ReadAllTextAsync(manifestPath, ct))
            ?? throw new InvalidDataException("Transfer manifest is invalid.");
        return (root, manifest);
    }

    private static string ResolveInside(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative))
            throw new InvalidDataException("Manifest file path must be relative.");
        var path = Path.GetFullPath(Path.Combine(root, relative));
        var relation = Path.GetRelativePath(root, path);
        if (relation == ".." || relation.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException("Manifest file path escapes the bundle.");
        return path;
    }

    private static async Task<string> Sha256Async(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
    }

    private static string NormalizeProvider(string provider) => provider.Trim().ToLowerInvariant() switch
    {
        "mongo" or "mongodb" => "Mongo",
        "postgres" or "postgresql" => "PostgreSql",
        _ => throw new ArgumentException("Provider must be Mongo or PostgreSql.")
    };

    private static string RequireMongoDatabase(ProviderTarget target) =>
        string.IsNullOrWhiteSpace(target.MongoDatabase)
            ? throw new ArgumentException("Mongo provider requires --database.")
            : target.MongoDatabase.Trim();

    private static string Quote(string identifier) => $"\"{identifier.Replace("\"", "\"\"")}\"";
}
