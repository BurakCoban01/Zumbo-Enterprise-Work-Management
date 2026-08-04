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

    private void ValidateAttachmentShape(
        IntakeFormDefinitionDocument definition,
        IReadOnlyCollection<IntakeSubmissionValueDocument> values,
        IReadOnlyCollection<IntakeAttachmentUpload> attachments)
    {
        if (attachments.Count > options.MaxAttachments)
        {
            throw new ValidationException(
                $"A submission cannot contain more than {options.MaxAttachments} attachments.");
        }
        if (attachments.Sum(x => x.SizeBytes) > options.MaxTotalAttachmentBytes)
        {
            throw new ValidationException("Submission attachments exceed the total size limit.");
        }

        var attachmentFields = definition.Fields
            .Where(x => x.Type == IntakeFieldTypes.Attachment)
            .ToDictionary(x => x.Key, StringComparer.Ordinal);
        foreach (var attachment in attachments)
        {
            var key = RequiredKey(attachment.FieldKey);
            if (!attachmentFields.ContainsKey(key))
            {
                throw new ValidationException(
                    $"Attachment field '{key}' is not defined.");
            }
            if (attachment.SizeBytes is <= 0 || attachment.SizeBytes > options.MaxAttachmentBytes)
            {
                throw new ValidationException(
                    $"Each attachment must contain between 1 and {options.MaxAttachmentBytes} bytes.");
            }
        }
        foreach (var field in attachmentFields.Values.Where(x => x.Required))
        {
            if (attachments.All(x => RequiredKey(x.FieldKey) != field.Key))
            {
                throw new ValidationException($"Field '{field.Label}' requires an attachment.");
            }
        }
    }
}
