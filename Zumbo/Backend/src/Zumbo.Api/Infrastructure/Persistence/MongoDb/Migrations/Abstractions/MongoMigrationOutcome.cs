using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed record MongoMigrationOutcome(
    string MigrationId,
    string Status,
    long Examined = 0,
    long Changed = 0,
    long Skipped = 0);
