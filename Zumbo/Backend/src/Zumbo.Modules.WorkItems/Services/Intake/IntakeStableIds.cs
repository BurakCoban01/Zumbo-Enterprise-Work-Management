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

public static class IntakeStableIds
{
    public static string FormVersionId(string formId, int version) =>
        Hash($"form-version\u001f{formId}\u001f{version}")[..32];

    public static string SubmissionId(
        string organizationId,
        string formId,
        int version,
        string submittedBy,
        string idempotencyKeyHash) =>
        Hash($"submission\u001f{organizationId}\u001f{formId}\u001f{version}\u001f{submittedBy}\u001f{idempotencyKeyHash}")[..32];

    public static string WorkItemId(string submissionId) =>
        Hash($"intake-work-item\u001f{submissionId}")[..32];

    public static string ConfirmationCode(string submissionId) =>
        "ZMB-" + submissionId[..8].ToUpperInvariant();

    public static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
