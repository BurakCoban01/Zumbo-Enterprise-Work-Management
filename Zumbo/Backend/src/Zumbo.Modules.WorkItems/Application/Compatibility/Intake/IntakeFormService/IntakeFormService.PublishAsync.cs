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

    public async Task<IntakeFormResponse> PublishAsync(
        string formId,
        string correlationId,
        CancellationToken ct)
    {
        var form = await GetManagedAsync(formId, PermissionCatalog.WorkflowManage, ct);
        EnsureNotArchived(form);
        await routePolicy.ValidateAsync(
            form.OrganizationId,
            form.ProjectId,
            form.Draft.BoardId,
            ct);
        var nextVersion = checked(form.PublishedVersion + 1);
        var published = new IntakeFormVersionDocument
        {
            Id = IntakeStableIds.FormVersionId(form.Id, nextVersion),
            OrganizationId = form.OrganizationId,
            FormId = form.Id,
            ProjectId = form.ProjectId,
            DefinitionVersion = nextVersion,
            Name = form.Name,
            Description = form.Description,
            Definition = CloneDefinition(form.Draft),
            PublishedByUserId = RequireUser(),
            PublishedAt = clock.UtcNow
        };
        await versions.CreateAsync(published, ct);

        var oldState = form.State;
        form.State = IntakeFormStates.Published;
        form.PublishedVersion = nextVersion;
        form.PublishedAccessPolicy = published.Definition.AccessPolicy;
        form.PublishedAt = published.PublishedAt;
        form.UpdatedAt = published.PublishedAt;
        form.UpdatedByUserId = published.PublishedByUserId;
        await ReplaceAsync(form, ct);
        await audit.WriteAsync(
            "IntakeFormPublished",
            "IntakeForm",
            form.Id,
            $"{oldState}:{nextVersion - 1}",
            $"{form.State}:{nextVersion}",
            correlationId,
            ct);
        return ToResponse(form);
    }
}
