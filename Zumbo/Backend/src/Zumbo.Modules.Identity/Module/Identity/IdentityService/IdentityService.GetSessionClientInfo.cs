using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Identity;

public sealed partial class IdentityService{

    private SessionClientInfo GetSessionClientInfo(RefreshSessionDocument? previousSession)
    {
        var supplied = sessionClientContext?.GetClientInfo();
        var deviceName = NormalizeDeviceName(supplied?.DeviceName)
            ?? NormalizeDeviceName(previousSession?.DeviceName)
            ?? "Unknown client";
        var fingerprint = supplied?.ClientFingerprint?.Trim();
        if (string.IsNullOrWhiteSpace(fingerprint) || fingerprint.Length > 128)
        {
            fingerprint = previousSession?.ClientFingerprint ?? string.Empty;
        }

        return new SessionClientInfo(deviceName, fingerprint);
    }
}
