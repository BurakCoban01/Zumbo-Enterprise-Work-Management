using System.Linq.Expressions;
using System.Text;
using System.Text.Json;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Audit;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Notifications;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.Projects;
using Zumbo.Modules.Teams;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

public sealed partial class PrivacyDataProcessorAdapter{

    private static string DescribeWorkItemReference(
        WorkItemDocument item,
        string userId,
        WorkItemUserActivityReference? activity)
    {
        var references = new List<string>();
        if (item.AssigneeUserId == userId) references.Add("assignee");
        if (activity?.CommentAuthor == true || item.Comments.Any(x => x.AuthorUserId == userId)) references.Add("comment-author");
        if (activity?.CommentRevision == true || item.Comments.Any(x => x.History.Any(r => r.EditedByUserId == userId))) references.Add("comment-revision");
        if (activity?.Mention == true || item.Comments.Any(x => x.Mentions.Contains(userId))) references.Add("mention");
        if (activity?.WorkLog == true || item.WorkLogs.Any(x => x.UserId == userId)) references.Add("worklog");
        if (activity?.Approval == true || item.Approvals.Any(x => x.RequestedByUserId == userId || x.DecidedByUserId == userId)) references.Add("approval");
        if (activity?.Timeline == true || item.StatusHistory.Any(x => x.ChangedByUserId == userId)) references.Add("status-history");
        return string.Join(',', references);
    }
}
