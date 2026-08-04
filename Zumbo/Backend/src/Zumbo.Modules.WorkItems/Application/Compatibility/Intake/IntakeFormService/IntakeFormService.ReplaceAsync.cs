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

    private async Task ReplaceAsync(IntakeFormDocument form, CancellationToken ct)
    {
        var result = await forms.ReplaceByVersionAsync(
            x => x.Id == form.Id && x.OrganizationId == form.OrganizationId,
            form,
            expectedVersion.Consume(form.Version),
            ct);
        if (!result.Found)
        {
            throw new NotFoundException("INTAKE_FORM_NOT_FOUND", "Intake form was not found.");
        }

        form.Version = result.Version!.Value;
    }
}
