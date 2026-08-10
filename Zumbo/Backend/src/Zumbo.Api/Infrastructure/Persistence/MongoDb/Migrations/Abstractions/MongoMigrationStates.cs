using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public static class MongoMigrationStates
{
    public const string Running = "Running";
    public const string Paused = "Paused";
    public const string Completed = "Completed";
    public const string RolledBack = "RolledBack";
    public const string Busy = "Busy";
    public const string Skipped = "Skipped";
    public const string DryRun = "DryRun";
}
