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

    public async Task DeleteAttachmentAsync(WorkItemAttachmentActivityDocument attachment, CancellationToken ct)
    {
        ValidateOwnership(attachment.OrganizationId, attachment.ProjectId, attachment.WorkItemId);
        var deleted = await attachments.DeleteByFilterAsync(x => x.Id == attachment.Id
            && x.OrganizationId == attachment.OrganizationId
            && x.ProjectId == attachment.ProjectId
            && x.WorkItemId == attachment.WorkItemId
            && x.Version == attachment.Version, ct);
        if (deleted == 0)
        {
            throw new ConflictException("ATTACHMENT_CONCURRENTLY_CHANGED", "Attachment changed before it could be deleted.");
        }
    }
}
