using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.Modules.Identity;

public static class PrivacyWorkflowEventTypes
{
    public const string Process = "identity.privacy-workflow.v1";
}
