using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using ApplicationPersistence = Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.BuildingBlocks.Infrastructure.Persistence;

internal static class DocumentVersion
{
    public static void Initialize<TDocument>(TDocument document)
    {
        if (document is ApplicationPersistence.IVersionedDocument versioned && versioned.Version <= 0)
        {
            versioned.Version = 1;
        }
    }

    public static long Read<TDocument>(TDocument document) =>
        document is ApplicationPersistence.IVersionedDocument versioned
            ? versioned.Version
            : throw NotVersioned<TDocument>();

    public static void Write<TDocument>(TDocument document, long version)
    {
        if (document is not ApplicationPersistence.IVersionedDocument versioned)
        {
            throw NotVersioned<TDocument>();
        }

        versioned.Version = version;
    }

    public static void ValidateExpected<TDocument>(long expectedVersion)
    {
        if (!typeof(ApplicationPersistence.IVersionedDocument).IsAssignableFrom(typeof(TDocument)))
        {
            throw NotVersioned<TDocument>();
        }

        if (expectedVersion <= 0 || expectedVersion == long.MaxValue)
        {
            throw new ApplicationPersistence.DocumentQueryException(
                "Expected document version must be between 1 and Int64.MaxValue - 1.");
        }
    }

    public static FieldDefinition<TDocument, long> Field<TDocument>() =>
        new StringFieldDefinition<TDocument, long>(nameof(ApplicationPersistence.IVersionedDocument.Version));

    private static ApplicationPersistence.DocumentQueryException NotVersioned<TDocument>() =>
        new($"{typeof(TDocument).Name} must implement IVersionedDocument for compare-and-swap operations.");
}
