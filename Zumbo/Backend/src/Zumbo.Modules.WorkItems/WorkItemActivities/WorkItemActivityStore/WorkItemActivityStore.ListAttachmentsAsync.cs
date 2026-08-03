using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemActivityStore{

    public Task<WorkItemActivityPage<AttachmentResponse>> ListAttachmentsAsync(
        string organizationId, string projectId, string workItemId, int page, int pageSize, CancellationToken ct) =>
        PageAsync(attachments,
            x => x.OrganizationId == organizationId && x.ProjectId == projectId && x.WorkItemId == workItemId,
            x => x.CreatedAt,
            x => new AttachmentResponse(
                x.Id, x.FileName, x.ContentType, x.SizeBytes, x.CreatedAt,
                x.SecurityState, x.ScanProvider, x.ScannedAt),
            page, pageSize, ct);
}
