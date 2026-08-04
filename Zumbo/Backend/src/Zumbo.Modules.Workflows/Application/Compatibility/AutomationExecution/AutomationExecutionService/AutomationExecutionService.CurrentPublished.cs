using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed partial class AutomationExecutionService{

    private static AutomationRuleVersionDocument CurrentPublished(AutomationRuleDocument rule) =>
        rule.PublishedVersions.Single(version => version.Number == rule.PublishedVersion);
}
