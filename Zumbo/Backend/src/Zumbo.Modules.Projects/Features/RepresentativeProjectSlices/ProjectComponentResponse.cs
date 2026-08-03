using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;
public sealed record ProjectComponentResponse(string Id, string Name, string? Description, bool Archived);
