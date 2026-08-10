using System.Linq.Expressions;
using System.Text;
using System.Text.Json;
using Zumbo.Api.Infrastructure.Adapters.Platform.PlatformCore.PrivacyDataProcessorAdapter;
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
        PrivacyReferenceDescriptions.DescribeActivityReference(activity);

    private static string DescribeWorkItemReference(
        WorkItemDocument item,
        string userId,
        WorkItemUserActivityReference? activity)
        => PrivacyReferenceDescriptions.DescribeWorkItemReference(item, userId, activity);
}
