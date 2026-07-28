using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class DevelopmentIntegrationService(
    IDocumentRepository<DevelopmentConnectionDocument> connections,
    IDocumentRepository<DevelopmentRepositoryMappingDocument> mappings,
    IDocumentRepository<WorkItemDevelopmentLinkDocument> links,
    IDocumentRepository<DevelopmentWebhookReceiptDocument> receipts,
    IDocumentRepository<WorkItemDocument> workItems,
    IDevelopmentCredentialProtector credentialProtector,
    IDevelopmentIntegrationAuthorization authorization,
    IDevelopmentProjectDirectory projectDirectory,
    IDevelopmentProviderGateway providerGateway,
    IDevelopmentWebhookQueue webhookQueue,
    IProjectPermissionChecker projectPermissions,
    IWorkItemAuditPublisher audit,
    IClock clock,
    ICurrentUser currentUser)
{
    public async Task<DevelopmentConnectionReceipt> CreateAsync(
        CreateDevelopmentConnectionRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var organizationId = RequireOrganization();
        await authorization.EnsureCanManageAsync(organizationId, ct);
        if (await connections.CountByFilterAsync(
                item => item.OrganizationId == organizationId,
                ct) >= DevelopmentIntegrationLimits.MaximumConnectionsPerOrganization)
        {
            throw new ValidationException(
                $"An organization cannot contain more than {DevelopmentIntegrationLimits.MaximumConnectionsPerOrganization} development connections.");
        }

        var provider = NormalizeProvider(request.Provider);
        var credential = RequireSecret(request.AccessToken, "Provider access token");
        var webhookSecret = GenerateWebhookSecret(provider);
        var baseUrl = NormalizeBaseUrl(provider, request.BaseUrl);
        await providerGateway.ValidateBaseUrlAsync(provider, baseUrl, ct);
        var now = clock.UtcNow;
        var document = await connections.CreateAsync(new DevelopmentConnectionDocument
        {
            OrganizationId = organizationId,
            Name = Required(request.Name, "Connection name", 100),
            Provider = provider,
            BaseUrl = baseUrl,
            CredentialProtected = credentialProtector.Protect(credential),
            CredentialFingerprint = Fingerprint(credential),
            WebhookSecretProtected = credentialProtector.Protect(webhookSecret),
            WebhookSecretFingerprint = Fingerprint(webhookSecret),
            CreatedByUserId = RequireUser(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        }, ct);
        await WriteAuditAsync(
            "DevelopmentConnectionCreated",
            "DevelopmentConnection",
            document.Id,
            null,
            $"{document.Provider}|{new Uri(document.BaseUrl).Host}|{document.CredentialFingerprint}",
            correlationId,
            ct);
        return new DevelopmentConnectionReceipt(ToResponse(document), webhookSecret);
    }

    public async Task<IReadOnlyCollection<DevelopmentConnectionResponse>> ListAsync(
        CancellationToken ct)
    {
        var organizationId = RequireOrganization();
        await authorization.EnsureCanManageAsync(organizationId, ct);
        var documents = await ListAllAsync(
            connections,
            item => item.OrganizationId == organizationId,
            ct);
        return documents
            .OrderByDescending(item => item.UpdatedAtUtc)
            .Select(ToResponse)
            .ToList();
    }

    public async Task<DevelopmentConnectionResponse> GetAsync(
        string connectionId,
        CancellationToken ct) =>
        ToResponse(await GetManagedConnectionAsync(connectionId, ct));

    public async Task<DevelopmentConnectionResponse> RotateCredentialAsync(
        string connectionId,
        RotateDevelopmentCredentialRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var connection = await GetManagedConnectionAsync(connectionId, ct);
        EnsureConnected(connection);
        var credential = RequireSecret(request.AccessToken, "Provider access token");
        var previous = connection.CredentialFingerprint;
        connection.CredentialProtected = credentialProtector.Protect(credential);
        connection.CredentialFingerprint = Fingerprint(credential);
        connection.HealthStatus = "NotChecked";
        connection.HealthErrorCode = null;
        connection.HealthCheckedAtUtc = null;
        connection.UpdatedAtUtc = clock.UtcNow;
        await ReplaceConnectionAsync(connection, request.ExpectedVersion, ct);
        await WriteAuditAsync(
            "DevelopmentCredentialRotated",
            "DevelopmentConnection",
            connection.Id,
            previous,
            connection.CredentialFingerprint,
            correlationId,
            ct);
        return ToResponse(connection);
    }

    public async Task<DevelopmentConnectionReceipt> RotateWebhookSecretAsync(
        string connectionId,
        DevelopmentVersionRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var connection = await GetManagedConnectionAsync(connectionId, ct);
        EnsureConnected(connection);
        var secret = GenerateWebhookSecret(connection.Provider);
        connection.PreviousWebhookSecretProtected = connection.WebhookSecretProtected;
        connection.PreviousWebhookSecretVersion = connection.WebhookSecretVersion;
        connection.PreviousWebhookSecretValidUntilUtc = clock.UtcNow.AddMinutes(15);
        connection.WebhookSecretProtected = credentialProtector.Protect(secret);
        connection.WebhookSecretFingerprint = Fingerprint(secret);
        connection.WebhookSecretVersion++;
        connection.UpdatedAtUtc = clock.UtcNow;
        await ReplaceConnectionAsync(connection, request.ExpectedVersion, ct);
        await WriteAuditAsync(
            "DevelopmentWebhookSecretRotated",
            "DevelopmentConnection",
            connection.Id,
            "previous-version",
            $"v{connection.WebhookSecretVersion}|{connection.WebhookSecretFingerprint}",
            correlationId,
            ct);
        return new DevelopmentConnectionReceipt(ToResponse(connection), secret);
    }

    public async Task<DevelopmentHealthResponse> CheckHealthAsync(
        string connectionId,
        string correlationId,
        CancellationToken ct)
    {
        var connection = await GetManagedConnectionAsync(connectionId, ct);
        EnsureConnected(connection);
        var result = await providerGateway.ProbeAsync(
            connection.Provider,
            connection.BaseUrl,
            credentialProtector.Unprotect(connection.CredentialProtected),
            ct);
        connection.HealthStatus = result.Healthy ? "Healthy" : "Degraded";
        connection.HealthErrorCode = result.Healthy
            ? null
            : Optional(result.SafeErrorCode, "Health error code", 80) ?? "PROVIDER_UNAVAILABLE";
        connection.HealthCheckedAtUtc = clock.UtcNow;
        connection.UpdatedAtUtc = clock.UtcNow;
        await ReplaceConnectionAsync(connection, connection.Version, ct);
        await WriteAuditAsync(
            "DevelopmentConnectionHealthChecked",
            "DevelopmentConnection",
            connection.Id,
            null,
            $"{connection.HealthStatus}|{connection.HealthErrorCode}",
            correlationId,
            ct);
        return new DevelopmentHealthResponse(
            connection.HealthStatus,
            connection.HealthErrorCode,
            connection.HealthCheckedAtUtc.Value);
    }

    public async Task<DevelopmentProviderRepositoryResult> ListProviderRepositoriesAsync(
        string connectionId,
        CancellationToken ct)
    {
        var connection = await GetManagedConnectionAsync(connectionId, ct);
        EnsureConnected(connection);
        return await providerGateway.ListRepositoriesAsync(
            connection.Provider,
            connection.BaseUrl,
            credentialProtector.Unprotect(connection.CredentialProtected),
            DevelopmentIntegrationLimits.MaximumProviderRepositories,
            ct);
    }

    public async Task<IReadOnlyCollection<DevelopmentRepositoryMappingResponse>> ListMappingsAsync(
        string connectionId,
        CancellationToken ct)
    {
        var connection = await GetManagedConnectionAsync(connectionId, ct);
        var documents = await ListAllAsync(
            mappings,
            item => item.OrganizationId == connection.OrganizationId
                && item.ConnectionId == connection.Id,
            ct);
        return documents
            .OrderBy(item => item.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.RepositoryFullName, StringComparer.OrdinalIgnoreCase)
            .Select(ToResponse)
            .ToList();
    }

    public async Task<DevelopmentRepositoryMappingResponse> CreateMappingAsync(
        string connectionId,
        CreateDevelopmentRepositoryMappingRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var connection = await GetManagedConnectionAsync(connectionId, ct);
        EnsureConnected(connection);
        if (await mappings.CountByFilterAsync(
                item => item.OrganizationId == connection.OrganizationId
                    && item.ConnectionId == connection.Id,
                ct) >= DevelopmentIntegrationLimits.MaximumMappingsPerConnection)
        {
            throw new ValidationException(
                $"A development connection cannot contain more than {DevelopmentIntegrationLimits.MaximumMappingsPerConnection} repository mappings.");
        }

        var userId = RequireUser();
        var projectId = Required(request.ProjectId, "Project id", 128);
        var projectAccess = await projectPermissions.EnsureCanAsync(
            userId,
            projectId,
            PermissionCatalog.WorkItemLink,
            ct);
        if (!string.Equals(projectAccess.OrganizationId, connection.OrganizationId, StringComparison.Ordinal))
            throw new NotFoundException(
                "DEVELOPMENT_CONNECTION_NOT_FOUND",
                "Development connection was not found.");
        var project = await projectDirectory.GetAsync(connection.OrganizationId, projectId, ct);
        var externalRepositoryId = Required(
            request.ExternalRepositoryId,
            "External repository id",
            200);
        if (await mappings.ExistsByFilterAsync(
                item => item.OrganizationId == connection.OrganizationId
                    && item.ConnectionId == connection.Id
                    && item.ExternalRepositoryId == externalRepositoryId,
                ct))
        {
            throw new ConflictException(
                "DEVELOPMENT_REPOSITORY_ALREADY_MAPPED",
                "The provider repository is already mapped for this connection.");
        }

        var now = clock.UtcNow;
        var document = await mappings.CreateAsync(new DevelopmentRepositoryMappingDocument
        {
            OrganizationId = connection.OrganizationId,
            ConnectionId = connection.Id,
            ProjectId = project.ProjectId,
            ProjectKey = project.ProjectKey,
            ProjectName = project.ProjectName,
            ExternalRepositoryId = externalRepositoryId,
            RepositoryName = Required(request.RepositoryName, "Repository name", 120),
            RepositoryFullName = Required(request.RepositoryFullName, "Repository full name", 240),
            RepositoryUrl = NormalizeRepositoryUrl(connection, request.RepositoryUrl),
            DefaultBranch = Required(request.DefaultBranch, "Default branch", 255),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        }, ct);
        await WriteAuditAsync(
            "DevelopmentRepositoryMapped",
            "DevelopmentRepositoryMapping",
            document.Id,
            null,
            $"{document.ProjectKey}|{document.RepositoryFullName}",
            correlationId,
            ct);
        return ToResponse(document);
    }

    public async Task DeleteMappingAsync(
        string mappingId,
        long expectedVersion,
        string correlationId,
        CancellationToken ct)
    {
        var mapping = await GetManagedMappingAsync(mappingId, ct);
        await projectPermissions.EnsureCanAsync(
            RequireUser(),
            mapping.ProjectId,
            PermissionCatalog.WorkItemLink,
            ct);
        if (mapping.Version != expectedVersion)
            throw MappingConflict();
        await links.DeleteByFilterAsync(
            item => item.OrganizationId == mapping.OrganizationId
                && item.MappingId == mapping.Id,
            ct);
        var deleted = await mappings.DeleteByFilterAsync(
            item => item.Id == mapping.Id
                && item.OrganizationId == mapping.OrganizationId
                && item.Version == expectedVersion,
            ct);
        if (deleted != 1) throw MappingConflict();
        await WriteAuditAsync(
            "DevelopmentRepositoryUnmapped",
            "DevelopmentRepositoryMapping",
            mapping.Id,
            $"{mapping.ProjectKey}|{mapping.RepositoryFullName}",
            null,
            correlationId,
            ct);
    }

    public async Task<DevelopmentConnectionResponse> DisconnectAsync(
        string connectionId,
        DevelopmentVersionRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var connection = await GetManagedConnectionAsync(connectionId, ct);
        if (!connection.IsConnected) return ToResponse(connection);
        connection.IsConnected = false;
        connection.LifecycleVersion++;
        connection.CredentialProtected = string.Empty;
        connection.WebhookSecretProtected = string.Empty;
        connection.PreviousWebhookSecretProtected = null;
        connection.PreviousWebhookSecretVersion = null;
        connection.PreviousWebhookSecretValidUntilUtc = null;
        connection.HealthStatus = "Disconnected";
        connection.HealthErrorCode = null;
        connection.DisconnectedAtUtc = clock.UtcNow;
        connection.UpdatedAtUtc = clock.UtcNow;
        await ReplaceConnectionAsync(connection, request.ExpectedVersion, ct);
        var ownedMappings = await ListAllAsync(
            mappings,
            item => item.OrganizationId == connection.OrganizationId
                && item.ConnectionId == connection.Id
                && item.IsActive,
            ct);
        foreach (var mapping in ownedMappings)
        {
            mapping.IsActive = false;
            mapping.UpdatedAtUtc = clock.UtcNow;
            await ReplaceMappingAsync(mapping, mapping.Version, ct);
        }
        await WriteAuditAsync(
            "DevelopmentConnectionDisconnected",
            "DevelopmentConnection",
            connection.Id,
            "Connected",
            "Disconnected",
            correlationId,
            ct);
        return ToResponse(connection);
    }

    public async Task DeleteConnectionAsync(
        string connectionId,
        long expectedVersion,
        string correlationId,
        CancellationToken ct)
    {
        var connection = await GetManagedConnectionAsync(connectionId, ct);
        if (connection.Version != expectedVersion) throw ConnectionConflict();
        await links.DeleteByFilterAsync(
            item => item.OrganizationId == connection.OrganizationId
                && item.ConnectionId == connection.Id,
            ct);
        await receipts.DeleteByFilterAsync(
            item => item.OrganizationId == connection.OrganizationId
                && item.ConnectionId == connection.Id,
            ct);
        await mappings.DeleteByFilterAsync(
            item => item.OrganizationId == connection.OrganizationId
                && item.ConnectionId == connection.Id,
            ct);
        var deleted = await connections.DeleteByFilterAsync(
            item => item.Id == connection.Id
                && item.OrganizationId == connection.OrganizationId
                && item.Version == expectedVersion,
            ct);
        if (deleted != 1) throw ConnectionConflict();
        await WriteAuditAsync(
            "DevelopmentConnectionDeleted",
            "DevelopmentConnection",
            connection.Id,
            $"{connection.Provider}|{connection.CredentialFingerprint}",
            null,
            correlationId,
            ct);
    }

    public async Task<IReadOnlyCollection<WorkItemDevelopmentLinkResponse>> ListWorkItemLinksAsync(
        string workItemId,
        CancellationToken ct)
    {
        var (workItem, organizationId) = await GetWorkItemAsync(
            workItemId,
            PermissionCatalog.WorkItemView,
            ct);
        var documents = await ListAllAsync(
            links,
            item => item.OrganizationId == organizationId
                && item.ProjectId == workItem.ProjectId
                && item.WorkItemId == workItem.Id,
            ct);
        var connectionStates = await ConnectionStatesAsync(
            organizationId,
            documents.Select(item => item.ConnectionId),
            ct);
        return documents
            .OrderByDescending(item => item.UpdatedAtUtc)
            .Select(item => ToResponse(
                item,
                connectionStates.GetValueOrDefault(item.ConnectionId)))
            .ToList();
    }

    public async Task<IReadOnlyCollection<DevelopmentRepositoryMappingResponse>>
        ListWorkItemMappingsAsync(
            string workItemId,
            CancellationToken ct)
    {
        var (workItem, organizationId) = await GetWorkItemAsync(
            workItemId,
            PermissionCatalog.WorkItemLink,
            ct);
        var documents = await ListAllAsync(
            mappings,
            item => item.OrganizationId == organizationId
                && item.ProjectId == workItem.ProjectId
                && item.IsActive,
            ct);
        return documents
            .OrderBy(item => item.RepositoryFullName, StringComparer.OrdinalIgnoreCase)
            .Select(ToResponse)
            .ToList();
    }

    public async Task<WorkItemDevelopmentLinkResponse> CreateWorkItemLinkAsync(
        string workItemId,
        CreateWorkItemDevelopmentLinkRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var (workItem, organizationId) = await GetWorkItemAsync(
            workItemId,
            PermissionCatalog.WorkItemLink,
            ct);
        var mappingId = Required(request.MappingId, "Repository mapping id", 128);
        var mapping = await mappings.SelectAsync(
            item => item.Id == mappingId
                && item.OrganizationId == organizationId
                && item.ProjectId == workItem.ProjectId
                && item.IsActive,
            ct) ?? throw new NotFoundException(
                "DEVELOPMENT_REPOSITORY_MAPPING_NOT_FOUND",
                "Development repository mapping was not found.");
        var connection = await connections.SelectAsync(
            item => item.Id == mapping.ConnectionId
                && item.OrganizationId == organizationId
                && item.IsConnected,
            ct) ?? throw new ConflictException(
                "DEVELOPMENT_CONNECTION_DISCONNECTED",
                "The development connection is disconnected.");
        if (await links.CountByFilterAsync(
                item => item.OrganizationId == organizationId
                    && item.WorkItemId == workItem.Id,
                ct) >= DevelopmentIntegrationLimits.MaximumLinksPerWorkItem)
        {
            throw new ValidationException(
                $"A work item cannot contain more than {DevelopmentIntegrationLimits.MaximumLinksPerWorkItem} development links.");
        }

        var normalized = NormalizeLinkRequest(mapping, request);
        var id = StableId(
            connection.Id,
            mapping.Id,
            workItem.Id,
            normalized.Kind,
            normalized.ExternalId);
        var existing = await links.SelectAsync(
            item => item.Id == id && item.OrganizationId == organizationId,
            ct);
        if (existing is not null)
            return ToResponse(existing, connection.IsConnected);
        var now = clock.UtcNow;
        var document = await links.CreateAsync(new WorkItemDevelopmentLinkDocument
        {
            Id = id,
            OrganizationId = organizationId,
            ConnectionId = connection.Id,
            MappingId = mapping.Id,
            ProjectId = mapping.ProjectId,
            WorkItemId = workItem.Id,
            Provider = connection.Provider,
            RepositoryFullName = mapping.RepositoryFullName,
            Kind = normalized.Kind,
            ExternalId = normalized.ExternalId,
            Title = normalized.Title,
            Url = normalized.Url,
            Branch = normalized.Branch,
            CommitSha = normalized.CommitSha,
            Status = normalized.Status,
            Source = "Manual",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        }, ct);
        await WriteAuditAsync(
            "WorkItemDevelopmentLinkCreated",
            "WorkItem",
            workItem.Id,
            null,
            $"{document.Provider}|{document.RepositoryFullName}|{document.Kind}|{document.ExternalId}",
            correlationId,
            ct);
        return ToResponse(document, true);
    }

    public async Task DeleteWorkItemLinkAsync(
        string workItemId,
        string linkId,
        long expectedVersion,
        string correlationId,
        CancellationToken ct)
    {
        var (workItem, organizationId) = await GetWorkItemAsync(
            workItemId,
            PermissionCatalog.WorkItemLink,
            ct);
        var link = await links.SelectAsync(
            item => item.Id == linkId
                && item.OrganizationId == organizationId
                && item.ProjectId == workItem.ProjectId
                && item.WorkItemId == workItem.Id,
            ct) ?? throw LinkNotFound();
        if (link.Version != expectedVersion) throw LinkConflict();
        var deleted = await links.DeleteByFilterAsync(
            item => item.Id == link.Id
                && item.OrganizationId == organizationId
                && item.Version == expectedVersion,
            ct);
        if (deleted != 1) throw LinkConflict();
        await WriteAuditAsync(
            "WorkItemDevelopmentLinkDeleted",
            "WorkItem",
            workItem.Id,
            $"{link.Provider}|{link.RepositoryFullName}|{link.Kind}|{link.ExternalId}",
            null,
            correlationId,
            ct);
    }

    public async Task<DevelopmentWebhookResult> ReceiveWebhookAsync(
        string connectionId,
        DevelopmentWebhookRequest request,
        CancellationToken ct)
    {
        if (request.Payload.Length is < 1
            || request.Payload.Length > DevelopmentIntegrationLimits.MaximumWebhookPayloadBytes)
        {
            throw new ValidationException("Development webhook payload size is not supported.");
        }
        var deliveryId = Required(request.DeliveryId, "Webhook delivery id", 200);
        var eventName = Required(request.EventName, "Webhook event name", 120);
        var connection = await connections.SelectAsync(
            item => item.Id == connectionId && item.IsConnected,
            ct) ?? throw new UnauthorizedException("Development webhook could not be verified.");
        if (!VerifyWebhook(connection, request))
            throw new UnauthorizedException("Development webhook could not be verified.");

        var now = clock.UtcNow;
        _ = await receipts.DeleteByFilterAsync(
            item => item.ConnectionId == connection.Id
                && item.ExpiresAtUtc <= now.UtcDateTime,
            ct);
        var receiptId = StableId(connection.Id, deliveryId);
        var normalized = DevelopmentWebhookSecurity.Normalize(
            connection.Provider,
            eventName,
            request.Payload);
        var receipt = new DevelopmentWebhookReceiptDocument
        {
            Id = receiptId,
            OrganizationId = connection.OrganizationId,
            ConnectionId = connection.Id,
            DeliveryId = deliveryId,
            ProviderEvent = eventName,
            PayloadSha256 = Hash(request.Payload),
            ReceivedAtUtc = now,
            ExpiresAtUtc = now.AddDays(
                DevelopmentIntegrationLimits.DeliveryRetentionDays).UtcDateTime
        };
        try
        {
            await receipts.CreateAsync(receipt, ct);
        }
        catch (DocumentConflictException)
        {
            var existing = await receipts.SelectAsync(
                item => item.Id == receiptId
                    && item.ConnectionId == connection.Id,
                ct);
            if (existing is null
                || !CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(existing.PayloadSha256),
                    Encoding.ASCII.GetBytes(receipt.PayloadSha256)))
            {
                throw new ConflictException(
                    "DEVELOPMENT_WEBHOOK_DELIVERY_COLLISION",
                    "The webhook delivery id was already used with different content.");
            }
            return new DevelopmentWebhookResult("Duplicate", 0, true);
        }

        await webhookQueue.EnqueueAsync(new DevelopmentWebhookEvent(
            receipt.Id,
            connection.Id,
            connection.LifecycleVersion,
            connection.OrganizationId,
            deliveryId,
            eventName,
            normalized), ct);
        return new DevelopmentWebhookResult("Accepted", 0, false);
    }

    public async Task ProcessWebhookAsync(
        DevelopmentWebhookEvent message,
        CancellationToken ct)
    {
        var receipt = await receipts.SelectAsync(
            item => item.Id == message.ReceiptId
                && item.ConnectionId == message.ConnectionId
                && item.OrganizationId == message.OrganizationId,
            ct);
        if (receipt is null || receipt.Status == DevelopmentWebhookReceiptStatuses.Applied)
            return;
        var connection = await connections.SelectAsync(
            item => item.Id == message.ConnectionId
                && item.OrganizationId == message.OrganizationId
                && item.IsConnected,
            ct);
        if (connection is null
            || connection.LifecycleVersion != message.ConnectionLifecycleVersion)
        {
            receipt.Status = DevelopmentWebhookReceiptStatuses.Ignored;
            await ReplaceReceiptAsync(receipt, ct);
            return;
        }
        if (message.Event is null)
        {
            receipt.Status = DevelopmentWebhookReceiptStatuses.Ignored;
            await ReplaceReceiptAsync(receipt, ct);
            return;
        }
        var mapping = await mappings.SelectAsync(
            item => item.OrganizationId == connection.OrganizationId
                && item.ConnectionId == connection.Id
                && item.ExternalRepositoryId == message.Event.RepositoryExternalId
                && item.IsActive,
            ct);
        if (mapping is null)
        {
            receipt.Status = DevelopmentWebhookReceiptStatuses.Ignored;
            await ReplaceReceiptAsync(receipt, ct);
            return;
        }
        var normalizedEvent = NormalizeProviderEvent(mapping, message.Event);
        var applied = await ApplyProviderEventAsync(
            connection,
            mapping,
            normalizedEvent,
            message.DeliveryId,
            ct);
        receipt.Status = applied > 0
            ? DevelopmentWebhookReceiptStatuses.Applied
            : DevelopmentWebhookReceiptStatuses.Ignored;
        receipt.AppliedLinks = applied;
        await ReplaceReceiptAsync(receipt, ct);
        if (applied > 0)
        {
            await WriteAuditAsync(
                "DevelopmentWebhookApplied",
                "DevelopmentConnection",
                connection.Id,
                null,
                $"{message.ProviderEvent}|{normalizedEvent.Kind}|{applied}|{receipt.PayloadSha256[..16]}",
                message.DeliveryId,
                ct);
        }
    }

    private async Task<int> ApplyProviderEventAsync(
        DevelopmentConnectionDocument connection,
        DevelopmentRepositoryMappingDocument mapping,
        NormalizedDevelopmentEvent providerEvent,
        string deliveryId,
        CancellationToken ct)
    {
        var candidates = await ListAllAsync(
            links,
            item => item.OrganizationId == connection.OrganizationId
                && item.MappingId == mapping.Id
                && (item.ExternalId == providerEvent.ExternalId
                    || providerEvent.CommitSha != null
                        && item.CommitSha == providerEvent.CommitSha),
            ct);
        var createdLinkIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var workItem in await ResolveReferencedWorkItemsAsync(mapping, providerEvent, ct))
        {
            var id = StableId(
                connection.Id,
                mapping.Id,
                workItem.Id,
                providerEvent.Kind,
                providerEvent.ExternalId);
            if (candidates.All(item => item.Id != id))
            {
                var existing = await links.SelectAsync(
                    item => item.Id == id && item.OrganizationId == connection.OrganizationId,
                    ct);
                if (existing is not null)
                {
                    candidates.Add(existing);
                }
                else if (await links.CountByFilterAsync(
                    item => item.OrganizationId == connection.OrganizationId
                        && item.WorkItemId == workItem.Id,
                    ct) < DevelopmentIntegrationLimits.MaximumLinksPerWorkItem)
                {
                    var now = clock.UtcNow;
                    var created = await links.CreateAsync(new WorkItemDevelopmentLinkDocument
                    {
                        Id = id,
                        OrganizationId = connection.OrganizationId,
                        ConnectionId = connection.Id,
                        MappingId = mapping.Id,
                        ProjectId = mapping.ProjectId,
                        WorkItemId = workItem.Id,
                        Provider = connection.Provider,
                        RepositoryFullName = mapping.RepositoryFullName,
                        Kind = providerEvent.Kind,
                        ExternalId = providerEvent.ExternalId,
                        Title = providerEvent.Title,
                        Url = providerEvent.Url,
                        Branch = providerEvent.Branch,
                        CommitSha = providerEvent.CommitSha,
                        Status = providerEvent.Status,
                        Source = "Webhook",
                        LastEventAtUtc = providerEvent.OccurredAtUtc ?? now,
                        CreatedAtUtc = now,
                        UpdatedAtUtc = now
                    }, ct);
                    candidates.Add(created);
                    createdLinkIds.Add(created.Id);
                }
            }
        }

        var applied = 0;
        foreach (var link in candidates.DistinctBy(item => item.Id))
        {
            if (createdLinkIds.Contains(link.Id))
            {
                applied++;
                continue;
            }
            var eventTime = providerEvent.OccurredAtUtc ?? clock.UtcNow;
            if (link.LastEventAtUtc is not null && link.LastEventAtUtc > eventTime)
                continue;
            var before = $"{link.Status}|{link.Title}|{link.Url}|{link.Branch}|{link.CommitSha}";
            link.Title = providerEvent.Title;
            link.Url = providerEvent.Url;
            link.Branch = providerEvent.Branch ?? link.Branch;
            link.CommitSha = providerEvent.CommitSha ?? link.CommitSha;
            link.Status = providerEvent.Status;
            link.Source = "Webhook";
            link.LastEventAtUtc = eventTime;
            link.UpdatedAtUtc = clock.UtcNow;
            var after = $"{link.Status}|{link.Title}|{link.Url}|{link.Branch}|{link.CommitSha}";
            if (before == after && link.Version > 0) continue;
            try
            {
                var result = await links.ReplaceByVersionAsync(
                    item => item.Id == link.Id
                        && item.OrganizationId == link.OrganizationId,
                    link,
                    link.Version,
                    ct);
                if (result.Found)
                {
                    link.Version = result.Version!.Value;
                    applied++;
                }
            }
            catch (DocumentConcurrencyException)
            {
                var current = await links.SelectAsync(
                    item => item.Id == link.Id
                        && item.OrganizationId == link.OrganizationId,
                    ct);
                if (current is not null
                    && current.Status == providerEvent.Status
                    && current.Url == providerEvent.Url)
                {
                    continue;
                }
                throw new ConflictException(
                    "DEVELOPMENT_LINK_CONFLICT",
                    "Development link changed concurrently; retry the webhook delivery.");
            }
        }
        return applied;
    }

    private async Task<IReadOnlyCollection<WorkItemDocument>> ResolveReferencedWorkItemsAsync(
        DevelopmentRepositoryMappingDocument mapping,
        NormalizedDevelopmentEvent providerEvent,
        CancellationToken ct)
    {
        var prefixes = DevelopmentWebhookReferencePolicy
            .ExtractWithinLimit(providerEvent.ReferenceTexts)
            .Where(reference => string.Equals(
                reference.ProjectKey,
                mapping.ProjectKey,
                StringComparison.OrdinalIgnoreCase))
            .Select(reference => reference.WorkItemIdPrefix)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var result = new List<WorkItemDocument>();
        foreach (var prefix in prefixes)
        {
            var matches = await workItems.ListByFilterAsync(
                item => item.ProjectId == mapping.ProjectId
                    && item.Id.StartsWith(prefix)
                    && !item.Archived,
                item => item.Id,
                pageSize: 2,
                cancellationToken: ct);
            if (matches.Count == 1) result.Add(matches[0]);
        }
        return result;
    }

    private bool VerifyWebhook(
        DevelopmentConnectionDocument connection,
        DevelopmentWebhookRequest request)
    {
        if (DevelopmentWebhookSecurity.Verify(
                connection.Provider,
                credentialProtector.Unprotect(connection.WebhookSecretProtected),
                request,
                clock.UtcNow))
        {
            return true;
        }
        return connection.PreviousWebhookSecretProtected is not null
            && connection.PreviousWebhookSecretValidUntilUtc >= clock.UtcNow
            && DevelopmentWebhookSecurity.Verify(
                connection.Provider,
                credentialProtector.Unprotect(connection.PreviousWebhookSecretProtected),
                request,
                clock.UtcNow);
    }

    private async Task<DevelopmentConnectionDocument> GetManagedConnectionAsync(
        string connectionId,
        CancellationToken ct)
    {
        var organizationId = RequireOrganization();
        await authorization.EnsureCanManageAsync(organizationId, ct);
        return await connections.SelectAsync(
            item => item.Id == connectionId
                && item.OrganizationId == organizationId,
            ct) ?? throw new NotFoundException(
                "DEVELOPMENT_CONNECTION_NOT_FOUND",
                "Development connection was not found.");
    }

    private async Task<DevelopmentRepositoryMappingDocument> GetManagedMappingAsync(
        string mappingId,
        CancellationToken ct)
    {
        var organizationId = RequireOrganization();
        await authorization.EnsureCanManageAsync(organizationId, ct);
        return await mappings.SelectAsync(
            item => item.Id == mappingId
                && item.OrganizationId == organizationId,
            ct) ?? throw new NotFoundException(
                "DEVELOPMENT_REPOSITORY_MAPPING_NOT_FOUND",
                "Development repository mapping was not found.");
    }

    private async Task<(WorkItemDocument WorkItem, string OrganizationId)> GetWorkItemAsync(
        string workItemId,
        string permission,
        CancellationToken ct)
    {
        var userId = RequireUser();
        var workItem = await workItems.SelectAsync(
            item => item.Id == workItemId && !item.Archived,
            ct) ?? throw new NotFoundException(
                "WORK_ITEM_NOT_FOUND",
                "Work item was not found.");
        var access = await projectPermissions.EnsureCanAsync(
            userId,
            workItem.ProjectId,
            permission,
            ct);
        if (!string.Equals(access.OrganizationId, RequireOrganization(), StringComparison.Ordinal))
            throw new NotFoundException("WORK_ITEM_NOT_FOUND", "Work item was not found.");
        return (workItem, access.OrganizationId);
    }

    private async Task<Dictionary<string, bool>> ConnectionStatesAsync(
        string organizationId,
        IEnumerable<string> connectionIds,
        CancellationToken ct)
    {
        var result = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var connectionId in connectionIds.Distinct(StringComparer.Ordinal))
        {
            var connection = await connections.SelectAsync(
                item => item.Id == connectionId
                    && item.OrganizationId == organizationId,
                ct);
            result[connectionId] = connection?.IsConnected == true;
        }
        return result;
    }

    private async Task ReplaceConnectionAsync(
        DevelopmentConnectionDocument connection,
        long expectedVersion,
        CancellationToken ct)
    {
        try
        {
            var result = await connections.ReplaceByVersionAsync(
                item => item.Id == connection.Id
                    && item.OrganizationId == connection.OrganizationId,
                connection,
                expectedVersion,
                ct);
            if (!result.Found) throw new NotFoundException(
                "DEVELOPMENT_CONNECTION_NOT_FOUND",
                "Development connection was not found.");
            connection.Version = result.Version!.Value;
        }
        catch (DocumentConcurrencyException)
        {
            throw ConnectionConflict();
        }
    }

    private async Task ReplaceMappingAsync(
        DevelopmentRepositoryMappingDocument mapping,
        long expectedVersion,
        CancellationToken ct)
    {
        try
        {
            var result = await mappings.ReplaceByVersionAsync(
                item => item.Id == mapping.Id
                    && item.OrganizationId == mapping.OrganizationId,
                mapping,
                expectedVersion,
                ct);
            if (!result.Found) throw new NotFoundException(
                "DEVELOPMENT_REPOSITORY_MAPPING_NOT_FOUND",
                "Development repository mapping was not found.");
            mapping.Version = result.Version!.Value;
        }
        catch (DocumentConcurrencyException)
        {
            throw MappingConflict();
        }
    }

    private async Task ReplaceReceiptAsync(
        DevelopmentWebhookReceiptDocument receipt,
        CancellationToken ct)
    {
        try
        {
            var result = await receipts.ReplaceByVersionAsync(
                item => item.Id == receipt.Id
                    && item.ConnectionId == receipt.ConnectionId,
                receipt,
                receipt.Version,
                ct);
            if (!result.Found) return;
            receipt.Version = result.Version!.Value;
        }
        catch (DocumentConcurrencyException)
        {
            var current = await receipts.SelectAsync(
                item => item.Id == receipt.Id
                    && item.ConnectionId == receipt.ConnectionId,
                ct);
            if (current?.Status == receipt.Status
                && current.AppliedLinks == receipt.AppliedLinks)
            {
                return;
            }
            throw;
        }
    }

    private static async Task<List<TDocument>> ListAllAsync<TDocument>(
        IDocumentRepository<TDocument> repository,
        System.Linq.Expressions.Expression<Func<TDocument, bool>> filter,
        CancellationToken ct)
        where TDocument : class, IDocument
    {
        var result = new List<TDocument>();
        string? cursor = null;
        do
        {
            var page = await repository.ListByCursorAsync(filter, cursor, 200, ct);
            result.AddRange(page.Items);
            cursor = page.NextCursor;
        }
        while (cursor is not null);
        return result;
    }

    private static LinkValues NormalizeLinkRequest(
        DevelopmentRepositoryMappingDocument mapping,
        CreateWorkItemDevelopmentLinkRequest request) =>
        new(
            NormalizeKind(request.Kind),
            Required(request.ExternalId, "External development id", 300),
            Required(request.Title, "Development link title", 200),
            NormalizeLinkUrl(mapping.RepositoryUrl, request.Url),
            Optional(request.Branch, "Development branch", 255),
            Optional(request.CommitSha, "Development commit", 128),
            NormalizeStatus(request.Status));

    private static NormalizedDevelopmentEvent NormalizeProviderEvent(
        DevelopmentRepositoryMappingDocument mapping,
        NormalizedDevelopmentEvent source) =>
        source with
        {
            Url = NormalizeLinkUrl(mapping.RepositoryUrl, source.Url),
            Status = NormalizeStatus(source.Status)
        };

    private static string NormalizeProvider(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        var provider = DevelopmentProviders.All.FirstOrDefault(
            item => item.Equals(normalized, StringComparison.OrdinalIgnoreCase));
        return provider ?? throw new ValidationException(
            "Development provider must be GitHub or GitLab.");
    }

    private static string NormalizeBaseUrl(string provider, string value)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? provider == DevelopmentProviders.GitHub
                ? "https://api.github.com"
                : "https://gitlab.com/api/v4"
            : value.Trim().TrimEnd('/');
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttp
                && uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrWhiteSpace(uri.UserInfo)
            || !string.IsNullOrWhiteSpace(uri.Query)
            || !string.IsNullOrWhiteSpace(uri.Fragment)
            || string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new ValidationException(
                "Development provider base URL must be an absolute HTTP(S) URL without credentials, query or fragment.");
        }
        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    private static string NormalizeRepositoryUrl(
        DevelopmentConnectionDocument connection,
        string value)
    {
        var normalized = NormalizeHttpsUrl(value, "Repository URL");
        var providerHost = new Uri(connection.BaseUrl).Host;
        var repositoryHost = new Uri(normalized).Host;
        var allowed = repositoryHost.Equals(providerHost, StringComparison.OrdinalIgnoreCase)
            || connection.Provider == DevelopmentProviders.GitHub
            && providerHost.Equals("api.github.com", StringComparison.OrdinalIgnoreCase)
            && repositoryHost.Equals("github.com", StringComparison.OrdinalIgnoreCase);
        if (!allowed)
            throw new ValidationException(
                "Repository URL host must match the configured development provider.");
        return normalized;
    }

    private static string NormalizeLinkUrl(string repositoryUrl, string value)
    {
        var normalized = NormalizeHttpsUrl(value, "Development link URL");
        if (!new Uri(normalized).Host.Equals(
                new Uri(repositoryUrl).Host,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException(
                "Development link URL host must match the mapped repository.");
        }
        return normalized;
    }

    private static string NormalizeHttpsUrl(string value, string label)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > 2_048
            || !Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrWhiteSpace(uri.UserInfo)
            || !string.IsNullOrWhiteSpace(uri.Fragment)
            || string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new ValidationException($"{label} must be a safe absolute HTTPS URL.");
        }
        return uri.AbsoluteUri;
    }

    private static string NormalizeKind(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        var kind = DevelopmentLinkKinds.All.FirstOrDefault(
            item => item.Equals(normalized, StringComparison.OrdinalIgnoreCase));
        return kind ?? throw new ValidationException(
            "Development link kind is not supported.");
    }

    private static string NormalizeStatus(string value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "open" => "Open",
            "merged" => "Merged",
            "closed" => "Closed",
            "success" => "Success",
            "failed" => "Failed",
            "pending" => "Pending",
            "running" => "Running",
            "pushed" => "Pushed",
            "unknown" or "" or null => "Unknown",
            _ => throw new ValidationException("Development status is not supported.")
        };

    private static string Required(string value, string label, int maximum)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 || normalized.Length > maximum)
            throw new ValidationException(
                $"{label} must contain between 1 and {maximum} characters.");
        return normalized;
    }

    private static string? Optional(string? value, string label, int maximum)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized)) return null;
        if (normalized.Length > maximum)
            throw new ValidationException($"{label} cannot exceed {maximum} characters.");
        return normalized;
    }

    private static string RequireSecret(string value, string label)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is < 16 or > 512
            || normalized.Any(char.IsWhiteSpace))
        {
            throw new ValidationException(
                $"{label} must contain between 16 and 512 non-whitespace characters.");
        }
        return normalized;
    }

    private static void EnsureConnected(DevelopmentConnectionDocument connection)
    {
        if (!connection.IsConnected
            || string.IsNullOrWhiteSpace(connection.CredentialProtected)
            || string.IsNullOrWhiteSpace(connection.WebhookSecretProtected))
        {
            throw new ConflictException(
                "DEVELOPMENT_CONNECTION_DISCONNECTED",
                "The development connection is disconnected.");
        }
    }

    private string RequireOrganization() => currentUser.OrganizationId
        ?? throw new UnauthorizedException("Authenticated organization is required.");

    private string RequireUser() => currentUser.UserId
        ?? throw new UnauthorizedException("Authenticated user is required.");

    private Task WriteAuditAsync(
        string action,
        string entityType,
        string entityId,
        string? oldValue,
        string? newValue,
        string correlationId,
        CancellationToken ct) =>
        audit.WriteAsync(
            action,
            entityType,
            entityId,
            oldValue,
            newValue,
            correlationId,
            ct);

    private static string GenerateWebhookSecret(string provider)
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return provider == DevelopmentProviders.GitLab
            ? "whsec_" + Convert.ToBase64String(bytes)
            : "ghsec_" + Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
    }

    private static string Fingerprint(string value) => Hash(
        Encoding.UTF8.GetBytes(value))[..16];

    private static string StableId(params string[] values) =>
        Hash(Encoding.UTF8.GetBytes(string.Join('\u001f', values)))[..32];

    private static string Hash(byte[] value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private static IReadOnlyCollection<string> RequiredScopes(string provider) =>
        provider == DevelopmentProviders.GitHub
            ? ["metadata:read", "pull_requests:read", "commit_statuses:read"]
            : ["read_api"];

    private static DevelopmentConnectionResponse ToResponse(
        DevelopmentConnectionDocument document) =>
        new(
            document.Id,
            document.Name,
            document.Provider,
            document.BaseUrl,
            document.CredentialFingerprint,
            document.WebhookSecretFingerprint,
            document.WebhookSecretVersion,
            document.IsConnected,
            document.HealthStatus,
            document.HealthErrorCode,
            document.HealthCheckedAtUtc,
            document.DisconnectedAtUtc,
            RequiredScopes(document.Provider),
            document.CreatedAtUtc,
            document.UpdatedAtUtc,
            document.Version);

    private static DevelopmentRepositoryMappingResponse ToResponse(
        DevelopmentRepositoryMappingDocument document) =>
        new(
            document.Id,
            document.ConnectionId,
            document.ProjectId,
            document.ProjectKey,
            document.ProjectName,
            document.ExternalRepositoryId,
            document.RepositoryName,
            document.RepositoryFullName,
            document.RepositoryUrl,
            document.DefaultBranch,
            document.IsActive,
            document.CreatedAtUtc,
            document.UpdatedAtUtc,
            document.Version);

    private static WorkItemDevelopmentLinkResponse ToResponse(
        WorkItemDevelopmentLinkDocument document,
        bool connectionActive) =>
        new(
            document.Id,
            document.ConnectionId,
            document.MappingId,
            document.ProjectId,
            document.WorkItemId,
            document.Provider,
            document.RepositoryFullName,
            document.Kind,
            document.ExternalId,
            document.Title,
            document.Url,
            document.Branch,
            document.CommitSha,
            document.Status,
            document.Source,
            connectionActive,
            document.LastEventAtUtc,
            document.CreatedAtUtc,
            document.UpdatedAtUtc,
            document.Version);

    private static ConflictException ConnectionConflict() => new(
        "DEVELOPMENT_CONNECTION_CONFLICT",
        "Development connection changed concurrently; refresh and retry.");

    private static ConflictException MappingConflict() => new(
        "DEVELOPMENT_MAPPING_CONFLICT",
        "Development repository mapping changed concurrently; refresh and retry.");

    private static NotFoundException LinkNotFound() => new(
        "DEVELOPMENT_LINK_NOT_FOUND",
        "Development link was not found.");

    private static ConflictException LinkConflict() => new(
        "DEVELOPMENT_LINK_CONFLICT",
        "Development link changed concurrently; refresh and retry.");

    private sealed record LinkValues(
        string Kind,
        string ExternalId,
        string Title,
        string Url,
        string? Branch,
        string? CommitSha,
        string Status);

}
