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

    private async Task<IntakeFormDocument> GetManagedAsync(
        string formId,
        string permission,
        CancellationToken ct)
    {
        var form = await forms.SelectAsync(x => x.Id == formId, ct)
            ?? throw new NotFoundException("INTAKE_FORM_NOT_FOUND", "Intake form was not found.");
        var authorization = await permissions.EnsureCanAsync(
            RequireUser(),
            form.ProjectId,
            permission,
            ct);
        if (authorization.OrganizationId != form.OrganizationId)
        {
            throw new NotFoundException("INTAKE_FORM_NOT_FOUND", "Intake form was not found.");
        }

        return form;
    }
}
