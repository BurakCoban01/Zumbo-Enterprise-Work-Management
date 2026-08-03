using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public interface IAutomationScheduledSourceProvider
{
    Task<IReadOnlyCollection<AutomationScheduledSource>> ListAsync(
        string projectId,
        int maximumSources,
        CancellationToken ct);
}
