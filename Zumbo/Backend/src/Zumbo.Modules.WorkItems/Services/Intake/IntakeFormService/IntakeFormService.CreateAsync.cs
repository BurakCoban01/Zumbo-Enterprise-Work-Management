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

    public async Task<IntakeFormResponse> CreateAsync(
        CreateIntakeFormRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var userId = RequireUser();
        var authorization = await permissions.EnsureCanAsync(
            userId,
            Required(request.ProjectId, "Project id", 128),
            PermissionCatalog.WorkflowManage,
            ct);
        var definition = NormalizeDefinition(request.Definition);
        await routePolicy.ValidateAsync(
            authorization.OrganizationId,
            authorization.ProjectId,
            definition.BoardId,
            ct);
        var now = clock.UtcNow;
        var document = new IntakeFormDocument
        {
            OrganizationId = authorization.OrganizationId,
            ProjectId = authorization.ProjectId,
            Name = Required(request.Name, "Form name", 120),
            Description = Optional(request.Description, 1_000),
            Draft = definition,
            CreatedByUserId = userId,
            UpdatedByUserId = userId,
            CreatedAt = now,
            UpdatedAt = now
        };
        await forms.CreateAsync(document, ct);
        await audit.WriteAsync(
            "IntakeFormCreated",
            "IntakeForm",
            document.Id,
            null,
            document.Name,
            correlationId,
            ct);
        return ToResponse(document);
    }
}
