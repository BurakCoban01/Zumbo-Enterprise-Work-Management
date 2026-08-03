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

    private async Task<IReadOnlyCollection<string>> FingerprintAttachmentsAsync(
        IReadOnlyCollection<IntakeAttachmentUpload> attachments,
        CancellationToken ct)
    {
        var result = new List<string>();
        foreach (var attachment in attachments)
        {
            if (!attachment.Content.CanSeek)
            {
                throw new ValidationException("Attachment content must support bounded replay.");
            }
            var originalPosition = attachment.Content.Position;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[64 * 1024];
            long total = 0;
            while (true)
            {
                var read = await attachment.Content.ReadAsync(buffer, ct);
                if (read == 0)
                {
                    break;
                }
                total += read;
                if (total > options.MaxAttachmentBytes)
                {
                    throw new ValidationException("Attachment content exceeds the size limit.");
                }
                hash.AppendData(buffer, 0, read);
            }
            attachment.Content.Position = originalPosition;
            if (total != attachment.SizeBytes)
            {
                throw new ValidationException("Attachment size does not match its content.");
            }
            result.Add(string.Join(
                "\u001f",
                RequiredKey(attachment.FieldKey),
                attachment.FileName,
                attachment.ContentType,
                total.ToString(CultureInfo.InvariantCulture),
                Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant()));
        }
        return result;
    }
}
