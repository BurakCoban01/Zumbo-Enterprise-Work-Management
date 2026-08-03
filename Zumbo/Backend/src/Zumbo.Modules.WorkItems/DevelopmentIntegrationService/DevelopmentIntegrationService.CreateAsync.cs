using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class DevelopmentIntegrationService{
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

}
