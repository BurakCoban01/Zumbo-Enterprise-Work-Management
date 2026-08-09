using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;
using Zumbo.Modules.WorkItems.Application.Features.Development.Connections;
using Zumbo.Modules.WorkItems.Application.Features.Development.Credentials;
using Zumbo.Modules.WorkItems.Application.Features.Development.ProviderHealth;
using Zumbo.Modules.WorkItems.Application.Features.Development.Repositories;
using Zumbo.Modules.WorkItems.Application.Features.Development.Mappings;
using Zumbo.Modules.WorkItems.Application.Features.Development.Links;
using Zumbo.Modules.WorkItems.Application.Features.Development.Webhooks;
using Zumbo.SharedKernel;

using static ApiEndpointResults;

internal static class DevelopmentIntegrationEndpoints
{
    internal static void MapDevelopmentIntegrationEndpoints(
        this RouteGroupBuilder api)
    {
        var management = api.MapGroup("/integrations/development")
            .WithTags("Development integrations")
            .RequireAuthorization()
            .WithZumboPermission(PermissionCatalog.IntegrationManage);
        management.AddEndpointFilter<WorkItemTransactionFilter>();

        management.MapPost("/", async (
            CreateDevelopmentConnectionRequest request,
            CreateConnectionHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Created(
                await handler.HandleAsync(
                    new CreateConnectionCommand(request, CorrelationId(http)),
                    ct),
                http));

        management.MapGet("/", async (
            ListConnectionsHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(new ListConnectionsQuery(), ct), http));

        management.MapGet("/{connectionId}", async (
            string connectionId,
            GetConnectionHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await handler.HandleAsync(new GetConnectionQuery(connectionId), ct), http));

        management.MapPost("/{connectionId}/rotate-credential", async (
            string connectionId,
            RotateDevelopmentCredentialRequest request,
            RotateCredentialHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(
                await handler.HandleAsync(
                    new RotateCredentialCommand(
                        connectionId,
                        request,
                        CorrelationId(http)),
                    ct),
                http))
            .RequireRateLimiting("bulk");

        management.MapPost("/{connectionId}/rotate-webhook-secret", async (
            string connectionId,
            DevelopmentVersionRequest request,
            RotateWebhookSecretHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(
                await handler.HandleAsync(
                    new RotateWebhookSecretCommand(
                        connectionId,
                        request,
                        CorrelationId(http)),
                    ct),
                http))
            .RequireRateLimiting("bulk");

        management.MapPost("/{connectionId}/health", async (
            string connectionId,
            CheckProviderHealthHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(
                await handler.HandleAsync(
                    new CheckProviderHealthCommand(
                        connectionId,
                        CorrelationId(http)),
                    ct),
                http))
            .RequireRateLimiting("bulk");

        management.MapGet("/{connectionId}/repositories", async (
            string connectionId,
            ListRepositoriesHandler handler,
            HttpContext http,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(
                new ListRepositoriesQuery(connectionId), ct);
            return Ok(
                new DevelopmentRepositoryPage(
                    result.Items
                        .Select(item => new DevelopmentRepositoryResponse(
                            item.ExternalRepositoryId,
                            item.Name,
                            item.FullName,
                            item.Url,
                            item.DefaultBranch))
                        .ToList(),
                    result.Partial ? "Partial" : "Complete"),
                http);
        }).RequireRateLimiting("bulk");

        management.MapGet("/{connectionId}/mappings", async (
            string connectionId,
            ListConnectionMappingsHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(
                await handler.HandleAsync(
                    new ListConnectionMappingsQuery(connectionId),
                    ct),
                http));

        management.MapPost("/{connectionId}/mappings", async (
            string connectionId,
            CreateDevelopmentRepositoryMappingRequest request,
            CreateMappingHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Created(
                await handler.HandleAsync(
                    new CreateMappingCommand(
                        connectionId,
                        request,
                        CorrelationId(http)),
                    ct),
                http));

        management.MapDelete("/mappings/{mappingId}", async (
            string mappingId,
            long expectedVersion,
            DeleteMappingHandler handler,
            HttpContext http,
            CancellationToken ct) =>
        {
            await handler.HandleAsync(
                new DeleteMappingCommand(
                    mappingId,
                    expectedVersion,
                    CorrelationId(http)),
                ct);
            return Results.NoContent();
        });

        management.MapPost("/{connectionId}/disconnect", async (
            string connectionId,
            DevelopmentVersionRequest request,
            DisconnectConnectionHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(
                await handler.HandleAsync(
                    new DisconnectConnectionCommand(
                        connectionId,
                        request,
                        CorrelationId(http)),
                    ct),
                http));

        management.MapDelete("/{connectionId}", async (
            string connectionId,
            long expectedVersion,
            DeleteConnectionHandler handler,
            HttpContext http,
            CancellationToken ct) =>
        {
            await handler.HandleAsync(
                new DeleteConnectionCommand(
                    connectionId,
                    expectedVersion,
                    CorrelationId(http)),
                ct);
            return Results.NoContent();
        });

        var workItemLinks = api.MapGroup(
                "/work-items/{workItemId}/development-links")
            .WithTags("Development integrations")
            .RequireAuthorization()
            .WithZumboPermission(PermissionCatalog.WorkItemView);
        workItemLinks.AddEndpointFilter<WorkItemTransactionFilter>();

        workItemLinks.MapGet("/", async (
            string workItemId,
            ListWorkItemLinksHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(
                await handler.HandleAsync(
                    new ListWorkItemLinksQuery(workItemId),
                    ct),
                http));

        workItemLinks.MapGet("/mappings", async (
            string workItemId,
            ListWorkItemMappingsHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Ok(
                await handler.HandleAsync(
                    new ListWorkItemMappingsQuery(workItemId),
                    ct),
                http))
            .WithZumboPermission(PermissionCatalog.WorkItemLink);

        workItemLinks.MapPost("/", async (
            string workItemId,
            CreateWorkItemDevelopmentLinkRequest request,
            CreateWorkItemLinkHandler handler,
            HttpContext http,
            CancellationToken ct) =>
            Created(
                await handler.HandleAsync(
                    new CreateWorkItemLinkCommand(
                        workItemId,
                        request,
                        CorrelationId(http)),
                    ct),
                http))
            .WithZumboPermission(PermissionCatalog.WorkItemLink);

        workItemLinks.MapDelete("/{linkId}", async (
            string workItemId,
            string linkId,
            long expectedVersion,
            DeleteWorkItemLinkHandler handler,
            HttpContext http,
            CancellationToken ct) =>
        {
            await handler.HandleAsync(
                new DeleteWorkItemLinkCommand(
                    workItemId,
                    linkId,
                    expectedVersion,
                    CorrelationId(http)),
                ct);
            return Results.NoContent();
        }).WithZumboPermission(PermissionCatalog.WorkItemLink);

        var ingress = api.MapGroup("/integrations/development")
            .WithTags("Development integrations");
        ingress.AddEndpointFilter<WorkItemTransactionFilter>();
        ingress.MapPost("/{connectionId}/webhook", ReceiveWebhookWithHandlerAsync)
            .AllowAnonymous();
    }

    private static async Task<IResult> ReceiveWebhookAsync(
        string connectionId,
        DevelopmentIntegrationService service,
        HttpContext http,
        CancellationToken ct)
    {
        var payload = await ReadPayloadAsync(http.Request, ct);
        var request = new DevelopmentWebhookRequest(
            Header(
                http.Request,
                200,
                "X-GitHub-Delivery",
                "webhook-id",
                "Idempotency-Key"),
            Header(
                http.Request,
                120,
                "X-GitHub-Event",
                "X-Gitlab-Event"),
            OptionalHeader(http.Request, 32, "webhook-timestamp"),
            Header(
                http.Request,
                2_048,
                "X-Hub-Signature-256",
                "webhook-signature"),
            payload);
        var result = await service.ReceiveWebhookAsync(
            connectionId,
            request,
            ct);
        return Results.Json(
            ApiResponse<DevelopmentWebhookResult>.Ok(
                result,
                CorrelationId(http)),
            statusCode: StatusCodes.Status202Accepted);
    }

    private static async Task<IResult> ReceiveWebhookWithHandlerAsync(
        string connectionId,
        ReceiveWebhookHandler handler,
        HttpContext http,
        CancellationToken ct)
    {
        var payload = await ReadPayloadAsync(http.Request, ct);
        var request = new DevelopmentWebhookRequest(
            Header(
                http.Request,
                200,
                "X-GitHub-Delivery",
                "webhook-id",
                "Idempotency-Key"),
            Header(
                http.Request,
                120,
                "X-GitHub-Event",
                "X-Gitlab-Event"),
            OptionalHeader(http.Request, 32, "webhook-timestamp"),
            Header(
                http.Request,
                2_048,
                "X-Hub-Signature-256",
                "webhook-signature"),
            payload);
        var result = await handler.HandleAsync(
            new ReceiveWebhookCommand(connectionId, request),
            ct);
        return Results.Json(
            ApiResponse<DevelopmentWebhookResult>.Ok(
                result,
                CorrelationId(http)),
            statusCode: StatusCodes.Status202Accepted);
    }

    private static async Task<byte[]> ReadPayloadAsync(
        HttpRequest request,
        CancellationToken ct)
    {
        if (request.ContentLength >
            DevelopmentIntegrationLimits.MaximumWebhookPayloadBytes)
        {
            throw new ValidationException(
                "Development webhook payload is too large.");
        }

        using var destination = new MemoryStream();
        var buffer = new byte[16_384];
        while (true)
        {
            var read = await request.Body.ReadAsync(buffer.AsMemory(), ct);
            if (read == 0) break;
            if (destination.Length + read >
                DevelopmentIntegrationLimits.MaximumWebhookPayloadBytes)
            {
                throw new ValidationException(
                    "Development webhook payload is too large.");
            }
            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        return destination.ToArray();
    }

    private static string Header(
        HttpRequest request,
        int maximum,
        params string[] names)
    {
        var value = OptionalHeader(request, maximum, names);
        return value ?? string.Empty;
    }

    private static string? OptionalHeader(
        HttpRequest request,
        int maximum,
        params string[] names)
    {
        foreach (var name in names)
        {
            var value = request.Headers[name].ToString().Trim();
            if (value.Length == 0) continue;
            if (value.Length > maximum)
            {
                throw new ValidationException(
                    "Development webhook header is too large.");
            }
            return value;
        }
        return null;
    }
}
