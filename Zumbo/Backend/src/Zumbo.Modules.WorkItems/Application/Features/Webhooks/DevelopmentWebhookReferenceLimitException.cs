using System.Text.RegularExpressions;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class DevelopmentWebhookReferenceLimitException()
    : ZumboException(
        "DEVELOPMENT_WEBHOOK_REFERENCE_LIMIT_EXCEEDED",
        $"Development webhook cannot contain more than {DevelopmentIntegrationLimits.MaximumWorkItemReferencesPerEvent} distinct work item references.");
