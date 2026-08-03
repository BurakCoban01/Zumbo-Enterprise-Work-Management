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

    public async Task<IntakeFormResponse> UpdateAsync(
        string formId,
        UpdateIntakeFormRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var form = await GetManagedAsync(formId, PermissionCatalog.WorkflowManage, ct);
        EnsureNotArchived(form);
        var definition = NormalizeDefinition(request.Definition);
        await routePolicy.ValidateAsync(
            form.OrganizationId,
            form.ProjectId,
            definition.BoardId,
            ct);
        var oldName = form.Name;
        form.Name = Required(request.Name, "Form name", 120);
        form.Description = Optional(request.Description, 1_000);
        form.Draft = definition;
        form.UpdatedByUserId = RequireUser();
        form.UpdatedAt = clock.UtcNow;
        await ReplaceAsync(form, ct);
        await audit.WriteAsync(
            "IntakeFormDraftUpdated",
            "IntakeForm",
            form.Id,
            oldName,
            form.Name,
            correlationId,
            ct);
        return ToResponse(form);
    }
}
