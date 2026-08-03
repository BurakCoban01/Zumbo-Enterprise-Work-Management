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

    private static bool SameOwner(
        WorkItemCommentActivityDocument x, string organizationId, string projectId, string workItemId) =>
        x.OrganizationId == organizationId && x.ProjectId == projectId && x.WorkItemId == workItemId;
    private static bool SameOwner(
        WorkItemCommentRevisionActivityDocument x, string organizationId, string projectId, string workItemId) =>
        x.OrganizationId == organizationId && x.ProjectId == projectId && x.WorkItemId == workItemId;
    private static bool SameOwner(
        WorkItemAttachmentActivityDocument x, string organizationId, string projectId, string workItemId) =>
        x.OrganizationId == organizationId && x.ProjectId == projectId && x.WorkItemId == workItemId;
    private static bool SameOwner(
        WorkItemWorkLogActivityDocument x, string organizationId, string projectId, string workItemId) =>
        x.OrganizationId == organizationId && x.ProjectId == projectId && x.WorkItemId == workItemId;
    private static bool SameOwner(
        WorkItemApprovalActivityDocument x, string organizationId, string projectId, string workItemId) =>
        x.OrganizationId == organizationId && x.ProjectId == projectId && x.WorkItemId == workItemId;
    private static bool SameOwner(
        WorkItemTimelineActivityDocument x, string organizationId, string projectId, string workItemId) =>
        x.OrganizationId == organizationId && x.ProjectId == projectId && x.WorkItemId == workItemId;
}
