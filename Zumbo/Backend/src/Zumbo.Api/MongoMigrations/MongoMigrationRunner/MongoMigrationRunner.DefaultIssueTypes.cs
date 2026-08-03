using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public sealed partial class MongoMigrationRunner{

    private static BsonArray DefaultIssueTypes() =>
    [
        IssueType("Epic", "Epic", "Epic", 0),
        IssueType("Story", "Story", "Standard", 10),
        IssueType("Task", "Task", "Standard", 20),
        IssueType("Bug", "Bug", "Standard", 30),
        IssueType("Subtask", "Subtask", "Subtask", 40)
    ];
}
