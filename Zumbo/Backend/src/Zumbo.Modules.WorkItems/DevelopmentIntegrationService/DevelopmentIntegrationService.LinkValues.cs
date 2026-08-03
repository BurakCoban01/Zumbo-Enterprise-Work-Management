using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class DevelopmentIntegrationService{

    private sealed record LinkValues(
        string Kind,
        string ExternalId,
        string Title,
        string Url,
        string? Branch,
        string? CommitSha,
        string Status);

}
