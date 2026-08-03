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

public sealed partial class IntakeFormService{

    private static string NormalizeTriageState(string value) => value?.Trim() switch
    {
        IntakeSubmissionStates.New => IntakeSubmissionStates.New,
        IntakeSubmissionStates.InReview => IntakeSubmissionStates.InReview,
        IntakeSubmissionStates.Resolved => IntakeSubmissionStates.Resolved,
        IntakeSubmissionStates.Rejected => IntakeSubmissionStates.Rejected,
        _ => throw new ValidationException(
            "Triage state must be New, InReview, Resolved or Rejected.")
    };
}
