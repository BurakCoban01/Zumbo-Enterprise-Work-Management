using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;

public static class MongoWorkItemGraphIndexes
{
    public static IReadOnlyList<MongoIndexSpecification> All { get; } =
    [
        new(
            "WorkItems",
            "workitemrelationedges",
            "ux_workitem_relation_edges_source",
            Keys(("ProjectId", 1), ("SourceWorkItemId", 1), ("TargetWorkItemId", 1), ("RelationType", 1)),
            Unique: true),
        new(
            "WorkItems",
            "workitemrelationedges",
            "ix_workitem_relation_edges_dependency_from",
            Keys(("ProjectId", 1), ("DependencyFromWorkItemId", 1), ("DependencyToWorkItemId", 1))),
        new(
            "WorkItems",
            "workitemrelationedges",
            "ix_workitem_relation_edges_dependency_to",
            Keys(("ProjectId", 1), ("DependencyToWorkItemId", 1), ("DependencyFromWorkItemId", 1))),
        new(
            "WorkItems",
            "workitems",
            "ix_workitems_project_parent_archived",
            Keys(("ProjectId", 1), ("ParentId", 1), ("Archived", 1), ("_id", 1)))
    ];

    private static BsonDocument Keys(params (string Name, object Value)[] keys)
    {
        var document = new BsonDocument();
        foreach (var (name, value) in keys)
        {
            document.Add(name, BsonValue.Create(value));
        }

        return document;
    }
}
