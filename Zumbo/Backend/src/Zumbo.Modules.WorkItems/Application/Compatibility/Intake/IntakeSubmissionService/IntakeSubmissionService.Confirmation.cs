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

    private static IntakeSubmissionConfirmationResponse Confirmation(
        IntakeSubmissionDocument submission,
        IntakeFormVersionDocument version) => new(
        submission.Id,
        submission.ConfirmationCode,
        version.Definition.ConfirmationMessage,
        submission.State,
        version.Definition.AccessPolicy == IntakeAccessPolicies.Public
            ? null
            : submission.WorkItemId);
}
