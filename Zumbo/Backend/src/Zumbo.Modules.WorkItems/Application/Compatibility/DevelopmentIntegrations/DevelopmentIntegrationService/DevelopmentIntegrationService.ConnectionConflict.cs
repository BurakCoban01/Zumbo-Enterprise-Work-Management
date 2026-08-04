using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class DevelopmentIntegrationService{

    private static ConflictException ConnectionConflict() => new(
        "DEVELOPMENT_CONNECTION_CONFLICT",
        "Development connection changed concurrently; refresh and retry.");

}
