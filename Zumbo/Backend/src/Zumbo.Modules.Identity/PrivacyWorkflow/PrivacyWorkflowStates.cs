using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Identity;

public static class PrivacyWorkflowStates
{
    public const string Pending = "Pending";
    public const string Running = "Running";
    public const string Failed = "Failed";
    public const string Completed = "Completed";
    public const string Expired = "Expired";
}
