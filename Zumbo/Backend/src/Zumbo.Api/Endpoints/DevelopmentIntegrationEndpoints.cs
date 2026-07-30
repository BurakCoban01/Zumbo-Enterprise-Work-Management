using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.Modules.WorkItems;
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
            DevelopmentIntegrationService service,
            HttpContext http,
            CancellationToken ct) =>
            Created(
                await service.CreateAsync(
                    request,
                    CorrelationId(http),
                    ct),
                http));

        management.MapGet("/", async (
            DevelopmentIntegrationService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.ListAsync(ct), http));

        management.MapGet("/{connectionId}", async (
            string connectionId,
            DevelopmentIntegrationService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.GetAsync(connectionId, ct), http));

        management.MapPost("/{connectionId}/rotate-credential", async (
            string connectionId,
            RotateDevelopmentCredentialRequest request,
            DevelopmentIntegrationService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(
                await service.RotateCredentialAsync(
                    connectionId,
                    request,
                    CorrelationId(http),
                    ct),
                http))
            .RequireRateLimiting("bulk");

        management.MapPost("/{connectionId}/rotate-webhook-secret", async (
            string connectionId,
            DevelopmentVersionRequest request,
            DevelopmentIntegrationService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(
                await service.RotateWebhookSecretAsync(
                    connectionId,
                    request,
                    CorrelationId(http),
                    ct),
                http))
            .RequireRateLimiting("bulk");

        management.MapPost("/{connectionId}/health", async (
            string connectionId,
            DevelopmentIntegrationService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(
                await service.CheckHealthAsync(
                    connectionId,
                    CorrelationId(http),
                    ct),
                http))
            .RequireRateLimiting("bulk");

        management.MapGet("/{connectionId}/repositories", async (
            string connectionId,
            DevelopmentIntegrationService service,
            HttpContext http,
            CancellationToken ct) =>
        {
            var result = await service.ListProviderRepositoriesAsync(
                connectionId,
                ct);
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
            DevelopmentIntegrationService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.ListMappingsAsync(connectionId, ct), http));

        management.MapPost("/{connectionId}/mappings", async (
            string connectionId,
            CreateDevelopmentRepositoryMappingRequest request,
            DevelopmentIntegrationService service,
            HttpContext http,
            CancellationToken ct) =>
            Created(
                await service.CreateMappingAsync(
                    connectionId,
                    request,
                    CorrelationId(http),
                    ct),
                http));

        management.MapDelete("/mappings/{mappingId}", async (
            string mappingId,
            long expectedVersion,
            DevelopmentIntegrationService service,
            HttpContext http,
            CancellationToken ct) =>
        {
            await service.DeleteMappingAsync(
                mappingId,
                expectedVersion,
                CorrelationId(http),
                ct);
            return Results.NoContent();
        });

        management.MapPost("/{connectionId}/disconnect", async (
            string connectionId,
            DevelopmentVersionRequest request,
            DevelopmentIntegrationService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(
                await service.DisconnectAsync(
                    connectionId,
                    request,
                    CorrelationId(http),
                    ct),
                http));

        management.MapDelete("/{connectionId}", async (
            string connectionId,
            long expectedVersion,
            DevelopmentIntegrationService service,
            HttpContext http,
            CancellationToken ct) =>
        {
            await service.DeleteConnectionAsync(
                connectionId,
                expectedVersion,
                CorrelationId(http),
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
            DevelopmentIntegrationService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.ListWorkItemLinksAsync(workItemId, ct), http));

        workItemLinks.MapGet("/mappings", async (
            string workItemId,
            DevelopmentIntegrationService service,
            HttpContext http,
            CancellationToken ct) =>
            Ok(await service.ListWorkItemMappingsAsync(workItemId, ct), http))
            .WithZumboPermission(PermissionCatalog.WorkItemLink);

        workItemLinks.MapPost("/", async (
            string workItemId,
            CreateWorkItemDevelopmentLinkRequest request,
            DevelopmentIntegrationService service,
            HttpContext http,
            CancellationToken ct) =>
            Created(
                await service.CreateWorkItemLinkAsync(
                    workItemId,
                    request,
                    CorrelationId(http),
                    ct),
                http))
            .WithZumboPermission(PermissionCatalog.WorkItemLink);

        workItemLinks.MapDelete("/{linkId}", async (
            string workItemId,
            string linkId,
            long expectedVersion,
            DevelopmentIntegrationService service,
            HttpContext http,
            CancellationToken ct) =>
        {
            await service.DeleteWorkItemLinkAsync(
                workItemId,
                linkId,
                expectedVersion,
                CorrelationId(http),
                ct);
            return Results.NoContent();
        }).WithZumboPermission(PermissionCatalog.WorkItemLink);

        var ingress = api.MapGroup("/integrations/development")
            .WithTags("Development integrations");
        ingress.AddEndpointFilter<WorkItemTransactionFilter>();
        ingress.MapPost("/{connectionId}/webhook", ReceiveWebhookAsync)
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
