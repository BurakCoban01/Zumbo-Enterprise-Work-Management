using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace Zumbo.BuildingBlocks.Infrastructure.Persistence;

/// <summary>
/// DateTimeOffset serializer that writes NEW values as a scalar Int64 UTC ticks value
/// instead of the legacy MongoDB .NET driver array representation [UtcTicks, OffsetMinutes].
///
/// The legacy array representation made the compound unique index
/// (RecurrenceId, ScheduledForUtc) multikey: every UTC document contributed an
/// additional (RecurrenceId, OffsetMinutes=0) index entry, so a second occurrence for
/// the same recurrence always collided with E11000 on the offset element.
///
/// Reads remain compatible with every representation that exists in this codebase:
/// scalar Int64/Int32 UTC ticks, BSON DateTime, the legacy [ticks, offset] array,
/// and the legacy document form carrying a "Ticks" member (see MongoMigrationRunner.Bson).
/// </summary>
public sealed class ScalarUtcTicksDateTimeOffsetSerializer : SerializerBase<DateTimeOffset>
{
    public static readonly ScalarUtcTicksDateTimeOffsetSerializer Instance = new();

    public override void Serialize(
        BsonSerializationContext context,
        BsonSerializationArgs args,
        DateTimeOffset value)
    {
        context.Writer.WriteInt64(value.ToUniversalTime().UtcTicks);
    }

    public override DateTimeOffset Deserialize(
        BsonDeserializationContext context,
        BsonDeserializationArgs args)
    {
        var reader = context.Reader;
        switch (reader.CurrentBsonType)
        {
            case BsonType.Int64:
                return FromTicks(reader.ReadInt64());

            case BsonType.Int32:
                return FromTicks(reader.ReadInt32());

            case BsonType.DateTime:
                var milliseconds = reader.ReadDateTime();
                var dateTime = DateTime.UnixEpoch.AddMilliseconds(milliseconds);
                return new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc));

            case BsonType.Array:
                return ReadLegacyArray(reader);

            case BsonType.Document:
                return ReadLegacyDocument(reader);

            default:
                throw new FormatException(
                    $"Cannot deserialize DateTimeOffset from BSON type {reader.CurrentBsonType}.");
        }
    }

    private static DateTimeOffset ReadLegacyArray(IBsonReader reader)
    {
        reader.ReadStartArray();
        reader.ReadBsonType();
        var ticks = reader.CurrentBsonType switch
        {
            BsonType.Int64 => reader.ReadInt64(),
            BsonType.Int32 => reader.ReadInt32(),
            _ => throw new FormatException(
                $"Legacy DateTimeOffset array element 0 has unsupported BSON type {reader.CurrentBsonType}.")
        };
        if (reader.State != BsonReaderState.EndOfArray)
        {
            reader.ReadBsonType();
            if (reader.State != BsonReaderState.EndOfArray)
            {
                reader.SkipValue();
            }
        }

        reader.ReadEndArray();
        return FromTicks(ticks);
    }

    private static DateTimeOffset ReadLegacyDocument(IBsonReader reader)
    {
        reader.ReadStartDocument();
        long? ticks = null;
        while (reader.ReadBsonType() != BsonType.EndOfDocument)
        {
            var name = reader.ReadName();
            if (name.Equals("Ticks", StringComparison.Ordinal))
            {
                ticks = reader.CurrentBsonType switch
                {
                    BsonType.Int64 => reader.ReadInt64(),
                    BsonType.Int32 => reader.ReadInt32(),
                    _ => throw new FormatException(
                        $"Legacy DateTimeOffset document 'Ticks' has unsupported BSON type {reader.CurrentBsonType}.")
                };
            }
            else
            {
                reader.SkipValue();
            }
        }

        reader.ReadEndDocument();
        return ticks is { } resolved
            ? FromTicks(resolved)
            : throw new FormatException("Legacy DateTimeOffset document does not contain a 'Ticks' member.");
    }

    private static DateTimeOffset FromTicks(long ticks) => new(ticks, TimeSpan.Zero);
}
