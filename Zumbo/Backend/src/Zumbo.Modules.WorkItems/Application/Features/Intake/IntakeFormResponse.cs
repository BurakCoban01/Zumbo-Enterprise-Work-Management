namespace Zumbo.Modules.WorkItems;

public sealed record IntakeFormResponse(
    string Id,
    string ProjectId,
    string Name,
    string Description,
    string State,
    string? PublicId,
    int PublishedVersion,
    IntakeFormDefinitionResponse Draft,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? PublishedAt,
    long Version) : Zumbo.BuildingBlocks.Application.Persistence.IVersionedResource;
