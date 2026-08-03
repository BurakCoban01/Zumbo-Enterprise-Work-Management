using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Persistence.PostgreSql;

public sealed class PostgreSqlPersistenceOptions
{
    private readonly Dictionary<Type, PostgreSqlDocumentStorage> mappings = [];

    public string ConnectionString { get; set; } = string.Empty;
    public int CommandTimeoutSeconds { get; set; } = 30;
    public int ConnectionTimeoutSeconds { get; set; } = 5;
    public int MinimumPoolSize { get; set; }
    public int MaximumPoolSize { get; set; } = 100;
    public JsonSerializerOptions JsonSerializerOptions { get; } = new(JsonSerializerDefaults.General)
    {
        PropertyNameCaseInsensitive = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    public IReadOnlyDictionary<Type, PostgreSqlDocumentStorage> DocumentMappings => mappings;

    public PostgreSqlPersistenceOptions MapDocument<TDocument>(string schema, string table)
        where TDocument : class, IDocument =>
        MapDocument(typeof(TDocument), schema, table);

    public PostgreSqlPersistenceOptions MapDocument(Type documentType, string schema, string table)
    {
        ArgumentNullException.ThrowIfNull(documentType);
        if (!typeof(IDocument).IsAssignableFrom(documentType) || !documentType.IsClass)
        {
            throw new ArgumentException(
                $"{documentType.FullName} must be a class implementing {nameof(IDocument)}.",
                nameof(documentType));
        }

        SqlIdentifier.Validate(schema, nameof(schema));
        SqlIdentifier.Validate(table, nameof(table));
        mappings[documentType] = new PostgreSqlDocumentStorage(schema, table);
        return this;
    }

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            throw new InvalidOperationException("A PostgreSQL connection string is required.");
        }

        _ = new NpgsqlConnectionStringBuilder(ConnectionString);
        if (CommandTimeoutSeconds is < 1 or > 600)
        {
            throw new InvalidOperationException("PostgreSQL command timeout must be between 1 and 600 seconds.");
        }

        if (ConnectionTimeoutSeconds is < 1 or > 300)
        {
            throw new InvalidOperationException("PostgreSQL connection timeout must be between 1 and 300 seconds.");
        }

        if (MinimumPoolSize < 0 || MaximumPoolSize < 1 || MinimumPoolSize > MaximumPoolSize)
        {
            throw new InvalidOperationException("PostgreSQL pool sizes are invalid.");
        }

        JsonSerializerOptions.MakeReadOnly();
    }

    internal PostgreSqlDocumentStorage Resolve(Type documentType) =>
        mappings.TryGetValue(documentType, out var configured)
            ? configured
            : throw new InvalidOperationException(
                $"No explicit PostgreSQL schema/table mapping exists for {documentType.FullName}.");
}
