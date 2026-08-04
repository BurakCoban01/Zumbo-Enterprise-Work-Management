using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class DevelopmentIntegrationService{

    private static ConflictException LinkConflict() => new(
        "DEVELOPMENT_LINK_CONFLICT",
        "Development link changed concurrently; refresh and retry.");

}
