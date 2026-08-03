using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed partial class MongoMigrationRunner{

    private static string? NormalizeGraphRelationType(string? relationType) =>
        relationType?.Trim().ToLowerInvariant() switch
        {
            "blocks" => "Blocks",
            "blockedby" or "blocked-by" => "BlockedBy",
            "relatesto" or "relates-to" => "RelatesTo",
            "duplicates" => "Duplicates",
            _ => null
        };
}
