using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Zumbo.Modules.Audit;
using Zumbo.Modules.Boards;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Notifications;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.Projects;
using Zumbo.Modules.Teams;
using Zumbo.Modules.Workflows;
using Zumbo.Modules.WorkItems;
using Zumbo.BuildingBlocks.Application.Security;

namespace Zumbo.ApiTests;

public sealed class RouteInventoryCharacterizationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public RouteInventoryCharacterizationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public void RuntimeRouteInventory_MatchesApprovedContract()
    {
        _ = _factory.CreateClient();

        var actual = string.Join(
            '\n',
            _factory.Services.GetServices<EndpointDataSource>()
                .SelectMany(source => source.Endpoints)
                .OfType<RouteEndpoint>()
                .SelectMany(Describe)
                .Order(StringComparer.Ordinal)) + '\n';

        var approvedPath = Path.Combine(BackendRoot(), "tests", "Zumbo.ApiTests", "RouteInventory.approved.txt");
        if (Environment.GetEnvironmentVariable("ZUMBO_APPROVE_ROUTE_INVENTORY") == "1")
        {
            File.WriteAllText(approvedPath, actual.Replace("\n", Environment.NewLine, StringComparison.Ordinal));
        }

        var expected = File.ReadAllText(approvedPath).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void EveryAuthenticatedApiRoute_HasCataloguedPermissionMetadata()
    {
        _ = _factory.CreateClient();
        var missing = new List<string>();
        var unknown = new List<string>();

        foreach (var endpoint in _factory.Services.GetServices<EndpointDataSource>()
                     .SelectMany(source => source.Endpoints)
                     .OfType<RouteEndpoint>()
                     .Where(endpoint => (endpoint.RoutePattern.RawText ?? string.Empty).StartsWith("/api/", StringComparison.Ordinal)))
        {
            var requiresAuthorization = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Count > 0;
            if (!requiresAuthorization)
            {
                continue;
            }

            var metadata = endpoint.Metadata.GetOrderedMetadata<EndpointPermissionMetadata>().LastOrDefault();
            if (metadata is null)
            {
                missing.Add(endpoint.RoutePattern.RawText ?? string.Empty);
            }
            else if (!PermissionCatalog.IsKnownEndpointPermission(metadata.Permission))
            {
                unknown.Add($"{endpoint.RoutePattern.RawText}:{metadata.Permission}");
            }
        }

        Assert.Empty(missing.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal));
        Assert.Empty(unknown.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void ModuleServices_StartAndPreserveScopedLifetimes()
    {
        _ = _factory.CreateClient();
        using var firstScope = _factory.Services.CreateScope();
        using var secondScope = _factory.Services.CreateScope();
        var scopedTypes = new[]
        {
            typeof(IdentityService), typeof(RegisterUserHandler), typeof(SearchUsersHandler),
            typeof(OrganizationService), typeof(CreateOrganizationHandler), typeof(ListOrganizationsHandler),
            typeof(TeamService), typeof(CreateTeamHandler), typeof(ListTeamsHandler),
            typeof(ProjectService), typeof(CreateProjectHandler), typeof(ListProjectsHandler),
            typeof(BoardService), typeof(CreateBoardHandler), typeof(ListBoardsByProjectHandler),
            typeof(NotificationService), typeof(ListNotificationsHandler), typeof(MarkNotificationAsReadHandler),
            typeof(AuditService), typeof(WriteAuditLogHandler), typeof(QueryAuditLogHandler),
            typeof(WorkflowService), typeof(UpsertWorkflowHandler), typeof(GetWorkflowHandler),
            typeof(WorkItemService), typeof(CreateWorkItemHandler), typeof(SearchWorkItemsHandler)
        };

        foreach (var serviceType in scopedTypes)
        {
            var first = firstScope.ServiceProvider.GetRequiredService(serviceType);
            Assert.Same(first, firstScope.ServiceProvider.GetRequiredService(serviceType));
            Assert.NotSame(first, secondScope.ServiceProvider.GetRequiredService(serviceType));
        }

        var boardPolicy = firstScope.ServiceProvider.GetRequiredService<BoardPolicyAdapter>();
        Assert.Same(boardPolicy, firstScope.ServiceProvider.GetRequiredService<IBoardColumnUsageChecker>());
        Assert.Same(boardPolicy, firstScope.ServiceProvider.GetRequiredService<IBoardPlacementPolicy>());
    }

    private static IEnumerable<string> Describe(RouteEndpoint endpoint)
    {
        var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["*"];
        var route = endpoint.RoutePattern.RawText ?? string.Empty;
        var authorization = DescribeAuthorization(endpoint);
        var permission = DescribePermission(endpoint);
        var rateLimit = DescribeRateLimit(endpoint);
        var tags = endpoint.Metadata
            .GetOrderedMetadata<ITagsMetadata>()
            .SelectMany(metadata => metadata.Tags)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);

        return methods.Select(method =>
            $"{method}|{route}|auth={authorization}|permission={permission}|rate={rateLimit}|tags={string.Join(',', tags)}");
    }

    private static string DescribePermission(RouteEndpoint endpoint)
    {
        var metadata = endpoint.Metadata.GetOrderedMetadata<EndpointPermissionMetadata>().LastOrDefault();
        return metadata is null ? "none" : $"{metadata.Permission}:{(metadata.IsGlobal ? "global" : "resource")}";
    }

    private static string DescribeAuthorization(RouteEndpoint endpoint)
    {
        if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            return "anonymous";
        }

        var requirements = endpoint.Metadata
            .GetOrderedMetadata<IAuthorizeData>()
            .Select(data => $"policy={data.Policy ?? ""};roles={data.Roles ?? ""};schemes={data.AuthenticationSchemes ?? ""}")
            .ToList();

        return requirements.Count == 0
            ? "none"
            : $"required[{string.Join('>', requirements)}]";
    }

    private static string DescribeRateLimit(RouteEndpoint endpoint)
    {
        var policies = endpoint.Metadata
            .GetOrderedMetadata<EnableRateLimitingAttribute>()
            .Select(attribute => attribute.PolicyName ?? "<instance>")
            .ToList();

        return policies.Count == 0
            ? "none"
            : $"effective={policies[^1]};chain={string.Join('>', policies)}";
    }

    private static string BackendRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Zumbo.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Backend root could not be located.");
    }
}
