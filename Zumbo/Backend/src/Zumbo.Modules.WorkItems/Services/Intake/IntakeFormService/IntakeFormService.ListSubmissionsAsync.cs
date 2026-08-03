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

    public async Task<IntakeSubmissionPage> ListSubmissionsAsync(
        string formId,
        string? state,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var form = await GetManagedAsync(formId, PermissionCatalog.WorkItemView, ct);
        var normalizedState = NormalizeOptionalSubmissionState(state);
        var safePage = Math.Max(page, 1);
        var safePageSize = Math.Clamp(pageSize, 1, 100);
        var filter = (System.Linq.Expressions.Expression<Func<IntakeSubmissionDocument, bool>>)(x =>
            x.OrganizationId == form.OrganizationId
            && x.FormId == form.Id
            && (normalizedState == null || x.State == normalizedState));
        var total = await submissions.CountByFilterAsync(filter, ct);
        var result = await submissions.ListByFilterAsync(
            filter,
            x => x.CreatedAt,
            orderDescending: true,
            page: safePage,
            pageSize: safePageSize,
            cancellationToken: ct);
        return new IntakeSubmissionPage(
            result.Select(ToSubmissionResponse).ToList(),
            safePage,
            safePageSize,
            total);
    }
}
