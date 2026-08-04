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

    private static string DescribeActivityReference(WorkItemUserActivityReference activity) =>
        string.Join(',', new[]
        {
            activity.CommentAuthor ? "comment-author" : null,
            activity.CommentRevision ? "comment-revision" : null,
            activity.Mention ? "mention" : null,
            activity.WorkLog ? "worklog" : null,
            activity.Approval ? "approval" : null,
            activity.Timeline ? "status-history" : null
        }.Where(value => value is not null));
}
