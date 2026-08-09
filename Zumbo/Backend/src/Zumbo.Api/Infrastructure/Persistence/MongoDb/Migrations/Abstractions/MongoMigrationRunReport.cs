using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed record MongoMigrationRunReport(
    bool DryRun,
    IReadOnlyList<MongoMigrationOutcome> Outcomes)
{
    public int Applied => Outcomes.Count(x => x.Status == MongoMigrationStates.Completed);
    public int Skipped => Outcomes.Count(x => x.Status == MongoMigrationStates.Skipped);
    public int Paused => Outcomes.Count(x => x.Status == MongoMigrationStates.Paused);
}
