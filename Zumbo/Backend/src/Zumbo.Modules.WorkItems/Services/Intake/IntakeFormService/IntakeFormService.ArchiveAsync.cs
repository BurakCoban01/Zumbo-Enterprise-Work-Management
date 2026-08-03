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

    public async Task<IntakeFormResponse> ArchiveAsync(
        string formId,
        string correlationId,
        CancellationToken ct)
    {
        var form = await GetManagedAsync(formId, PermissionCatalog.WorkflowManage, ct);
        if (form.State == IntakeFormStates.Archived)
        {
            return ToResponse(form);
        }

        var oldState = form.State;
        form.State = IntakeFormStates.Archived;
        form.ArchivedAt = clock.UtcNow;
        form.UpdatedAt = form.ArchivedAt.Value;
        form.UpdatedByUserId = RequireUser();
        await ReplaceAsync(form, ct);
        await audit.WriteAsync(
            "IntakeFormArchived",
            "IntakeForm",
            form.Id,
            oldState,
            form.State,
            correlationId,
            ct);
        return ToResponse(form);
    }
}
