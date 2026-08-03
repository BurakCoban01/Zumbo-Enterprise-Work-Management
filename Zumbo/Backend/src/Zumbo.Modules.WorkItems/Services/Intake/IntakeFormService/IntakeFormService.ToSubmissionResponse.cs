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

    internal static IntakeSubmissionResponse ToSubmissionResponse(
        IntakeSubmissionDocument source) => new(
        source.Id,
        source.FormId,
        source.FormVersion,
        source.ProjectId,
        source.State,
        source.ConfirmationCode,
        source.WorkItemId,
        source.Values.Select(x => new IntakeSubmissionValueDocument
        {
            FieldKey = x.FieldKey,
            Value = x.Value
        }).ToList(),
        source.Attachments.Select(x => new IntakeSubmissionAttachmentResponse(
            x.Id,
            x.FieldKey,
            x.FileName,
            x.ContentType,
            x.SizeBytes,
            x.SecurityState)).ToList(),
        source.TriageNote,
        source.TriagedByUserId,
        source.CreatedAt,
        source.UpdatedAt,
        source.Version);
}
