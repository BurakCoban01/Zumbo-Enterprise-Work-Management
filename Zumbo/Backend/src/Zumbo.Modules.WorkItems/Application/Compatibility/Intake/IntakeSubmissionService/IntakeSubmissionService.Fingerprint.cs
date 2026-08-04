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

    private static string Fingerprint(
        IntakeFormVersionDocument version,
        IReadOnlyCollection<IntakeSubmissionValueDocument> values,
        IReadOnlyCollection<string> attachments)
    {
        var canonical = new
        {
            version.FormId,
            version.DefinitionVersion,
            Values = values.OrderBy(x => x.FieldKey, StringComparer.Ordinal),
            Attachments = attachments.OrderBy(x => x, StringComparer.Ordinal)
        };
        return IntakeStableIds.Hash(JsonSerializer.Serialize(canonical, JsonOptions));
    }
}
