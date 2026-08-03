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

    internal async Task<IntakeFormVersionDocument> ResolveSubmissionVersionAsync(
        string identifier,
        bool publicAccess,
        CancellationToken ct)
    {
        var form = publicAccess
            ? await forms.SelectAsync(
                x => x.PublicId == identifier && x.State == IntakeFormStates.Published,
                ct)
            : await forms.SelectAsync(
                x => x.Id == identifier && x.State == IntakeFormStates.Published,
                ct);
        if (form is null)
        {
            throw new NotFoundException("INTAKE_FORM_NOT_FOUND", "Intake form was not found.");
        }

        var version = await GetVersionAsync(form, ct);
        if (publicAccess)
        {
            if (version.Definition.AccessPolicy != IntakeAccessPolicies.Public)
            {
                throw new NotFoundException("INTAKE_FORM_NOT_FOUND", "Intake form was not found.");
            }
        }
        else
        {
            _ = await permissions.EnsureCanAsync(
                RequireUser(),
                form.ProjectId,
                PermissionCatalog.WorkItemCreate,
                ct);
            if (version.Definition.AccessPolicy != IntakeAccessPolicies.Internal)
            {
                throw new ConflictException(
                    "INTAKE_FORM_PUBLIC_ONLY",
                    "Public forms must be submitted through the public intake route.");
            }
        }

        return version;
    }
}
