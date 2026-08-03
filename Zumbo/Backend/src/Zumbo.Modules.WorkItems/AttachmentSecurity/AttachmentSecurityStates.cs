using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public static class AttachmentSecurityStates
{
    public const string Quarantined = "Quarantined";
    public const string Clean = "Clean";
    public const string Rejected = "Rejected";
}
