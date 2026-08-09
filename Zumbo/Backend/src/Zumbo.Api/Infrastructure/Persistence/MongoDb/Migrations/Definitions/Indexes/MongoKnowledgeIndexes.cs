using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public static class MongoKnowledgeIndexes
{
    public static IReadOnlyList<MongoIndexSpecification> All { get; } =
    [
        new(
            "Projects",
            "knowledge_documents",
            "ix_knowledge_tenant_scope_state",
            new BsonDocument
            {
                ["OrganizationId"] = 1,
                ["ScopeType"] = 1,
                ["ScopeId"] = 1,
                ["Archived"] = 1,
                ["UpdatedAt"] = -1,
                ["_id"] = 1
            }),
        new(
            "Projects",
            "knowledge_documents",
            "ix_knowledge_tenant_owner_state",
            new BsonDocument
            {
                ["OrganizationId"] = 1,
                ["OwnerUserId"] = 1,
                ["Archived"] = 1,
                ["UpdatedAt"] = -1,
                ["_id"] = 1
            }),
        new(
            "Projects",
            "knowledge_documents",
            "ix_knowledge_tenant_tags",
            new BsonDocument
            {
                ["OrganizationId"] = 1,
                ["Tags"] = 1,
                ["Archived"] = 1,
                ["_id"] = 1
            })
    ];
}
