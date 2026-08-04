using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class DevelopmentIntegrationService{

    private static ConflictException MappingConflict() => new(
        "DEVELOPMENT_MAPPING_CONFLICT",
        "Development repository mapping changed concurrently; refresh and retry.");

}
