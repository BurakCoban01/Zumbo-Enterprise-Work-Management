using System.Globalization;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Runtime;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class IntakeSubmissionService{

    private static void EnsureSameRequest(
        IntakeSubmissionDocument submission,
        string fingerprint)
    {
        if (submission.RequestFingerprint != fingerprint)
        {
            throw new ConflictException(
                "IDEMPOTENCY_KEY_REUSED",
                "Idempotency key was already used for a different intake submission.");
        }
    }
}
