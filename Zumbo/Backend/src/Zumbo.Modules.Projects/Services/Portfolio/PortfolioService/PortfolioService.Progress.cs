using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class PortfolioService{

    private static int Progress(int completed, int total) =>
        total <= 0 ? 0 : Math.Clamp((int)Math.Round(completed * 100d / total), 0, 100);
}
