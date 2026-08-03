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

    private async Task TryDeleteStoredAsync(
        IEnumerable<IntakeSubmissionAttachmentDocument> attachments)
    {
        var result = await CompensationExecution.RunAsync(
            "intake.attachments.delete",
            async token =>
            {
                foreach (var attachment in attachments)
                {
                    await attachmentStorage.DeleteAsync(attachment.StoragePath, token);
                }
            });
        ObserveCompensation(result);
    }
}
