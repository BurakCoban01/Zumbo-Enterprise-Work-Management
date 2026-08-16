using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

namespace Zumbo.UnitTests;

public sealed class ScalarUtcTicksDateTimeOffsetSerializerTests
{
    private static readonly ScalarUtcTicksDateTimeOffsetSerializer Serializer =
        ScalarUtcTicksDateTimeOffsetSerializer.Instance;

    private static readonly DateTimeOffset Sample =
        new(2026, 7, 23, 13, 46, 0, TimeSpan.Zero);

    [Fact]
    public void Serialize_WritesScalarInt64UtcTicks_NotLegacyArray()
    {
        var document = new BsonDocument();
        using (var writer = new BsonDocumentWriter(
                   document,
                   new BsonDocumentWriterSettings()))
        {
            writer.WriteStartDocument();
            writer.WriteName("v");
            Serializer.Serialize(
                BsonSerializationContext.CreateRoot(writer),
                Sample);
            writer.WriteEndDocument();
        }

        var stored = document["v"];
        Assert.Equal(BsonType.Int64, stored.BsonType);
        Assert.Equal(Sample.UtcTicks, stored.AsInt64);
        Assert.NotEqual(BsonType.Array, stored.BsonType);
    }

    [Fact]
    public void Deserialize_ScalarInt64_RoundTripsToIdenticalUtcInstant()
    {
        var document = new BsonDocument("v", new BsonInt64(Sample.UtcTicks));
        var value = Deserialize(document);

        Assert.Equal(Sample, value);
        Assert.Equal(Sample.UtcTicks, value.UtcTicks);
        Assert.Equal(TimeSpan.Zero, value.Offset);
    }

    [Fact]
    public void Deserialize_LegacyArrayRepresentation_ReadsFirstElementAsUtcTicks()
    {
        var legacy = new BsonDocument(
            "v",
            new BsonArray { new BsonInt64(Sample.UtcTicks), new BsonInt32(0) });

        var value = Deserialize(legacy);

        Assert.Equal(Sample.UtcTicks, value.UtcTicks);
        Assert.Equal(TimeSpan.Zero, value.Offset);
    }

    [Fact]
    public void Deserialize_LegacyDocumentWithTicks_ReadsTicksMember()
    {
        var legacy = new BsonDocument(
            "v",
            new BsonDocument
            {
                ["Ticks"] = new BsonInt64(Sample.UtcTicks),
                ["OffsetMinutes"] = new BsonInt32(0)
            });

        var value = Deserialize(legacy);

        Assert.Equal(Sample.UtcTicks, value.UtcTicks);
    }

    [Fact]
    public void Deserialize_BsonDateTime_ReadsUtcInstant()
    {
        var document = new BsonDocument(
            "v",
            new BsonDateTime(new DateTime(
                Sample.Year,
                Sample.Month,
                Sample.Day,
                Sample.Hour,
                Sample.Minute,
                Sample.Second,
                DateTimeKind.Utc)));

        var value = Deserialize(document);

        Assert.Equal(Sample.ToUniversalTime(), value);
    }

    private static DateTimeOffset Deserialize(BsonDocument document)
    {
        using var reader = new BsonDocumentReader(document);
        reader.ReadStartDocument();
        reader.ReadName("v");
        var value = Serializer.Deserialize(
            BsonDeserializationContext.CreateRoot(reader),
            new BsonDeserializationArgs { NominalType = typeof(DateTimeOffset) });
        return value;
    }
}
