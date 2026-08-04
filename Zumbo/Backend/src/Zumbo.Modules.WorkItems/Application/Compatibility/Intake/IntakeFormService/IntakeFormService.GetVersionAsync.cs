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

    private async Task<IntakeFormVersionDocument> GetVersionAsync(
        IntakeFormDocument form,
        CancellationToken ct) =>
        await versions.SelectAsync(
            x => x.Id == IntakeStableIds.FormVersionId(form.Id, form.PublishedVersion)
                && x.OrganizationId == form.OrganizationId
                && x.FormId == form.Id,
            ct)
        ?? throw new ConflictException(
            "INTAKE_FORM_VERSION_MISSING",
            "The published intake form version is unavailable.");
}
