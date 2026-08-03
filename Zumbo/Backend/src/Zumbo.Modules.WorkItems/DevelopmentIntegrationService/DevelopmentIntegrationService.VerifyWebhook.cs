using System.Security.Cryptography;
using System.Text;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class DevelopmentIntegrationService{

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

}
