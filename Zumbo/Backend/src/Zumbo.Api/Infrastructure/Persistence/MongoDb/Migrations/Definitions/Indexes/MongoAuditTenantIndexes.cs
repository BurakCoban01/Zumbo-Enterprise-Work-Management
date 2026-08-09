using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public static class MongoAuditTenantIndexes
{
    public static IReadOnlyList<MongoIndexSpecification> All { get; } =
    [
        new("Audit", "auditlogs", "ix_auditlogs_organization_created",
            new BsonDocument { ["OrganizationId"] = 1, ["CreatedAt"] = -1, ["_id"] = 1 }),
        new("Audit", "auditlogs", "ix_auditlogs_organization_entity_created",
            new BsonDocument { ["OrganizationId"] = 1, ["EntityType"] = 1, ["EntityId"] = 1, ["CreatedAt"] = -1, ["_id"] = 1 }),
        new("Audit", "auditlogs", "ix_auditlogs_organization_actor_created",
            new BsonDocument { ["OrganizationId"] = 1, ["ActorUserId"] = 1, ["CreatedAt"] = -1, ["_id"] = 1 }),
        new("Audit", "auditlogs", "ux_auditlogs_organization_chain_sequence",
            new BsonDocument { ["OrganizationId"] = 1, ["ChainSequence"] = 1 },
            Unique: true,
            PartialFilter: new BsonDocument("ChainSequence", new BsonDocument("$gt", 0)))
    ];
}
