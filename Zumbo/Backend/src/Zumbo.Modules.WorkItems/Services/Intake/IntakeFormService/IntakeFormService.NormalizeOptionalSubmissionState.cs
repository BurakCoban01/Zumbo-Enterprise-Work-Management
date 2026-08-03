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

    private static string? NormalizeOptionalSubmissionState(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim() switch
        {
            IntakeSubmissionStates.Processing => IntakeSubmissionStates.Processing,
            IntakeSubmissionStates.New => IntakeSubmissionStates.New,
            IntakeSubmissionStates.InReview => IntakeSubmissionStates.InReview,
            IntakeSubmissionStates.Resolved => IntakeSubmissionStates.Resolved,
            IntakeSubmissionStates.Rejected => IntakeSubmissionStates.Rejected,
            _ => throw new ValidationException("Unknown intake submission state.")
        };
    }
}
