using MongoDB.Bson;

public static class MongoIdentityAuthorizationIndexes
{
    public static IReadOnlyList<MongoIndexSpecification> All { get; } =
    [
        new(
            "Identity",
            "identitypermissiondefinitions",
            "ux_identity_permission_key_ci",
            new BsonDocument("Key", 1),
            Unique: true,
            CaseInsensitive: true),
        new(
            "Identity",
            "identitypermissiondefinitions",
            "ix_identity_permission_active_order",
            new BsonDocument
            {
                ["IsActive"] = 1,
                ["DisplayOrder"] = 1,
                ["_id"] = 1
            })
    ];
}
