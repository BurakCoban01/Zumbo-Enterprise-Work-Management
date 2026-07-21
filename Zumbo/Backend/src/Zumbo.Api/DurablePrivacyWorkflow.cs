using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.Modules.Identity;

public static class PrivacyWorkflowEventTypes
{
    public const string Process = "identity.privacy-workflow.v1";
}

public sealed class DurablePrivacyWorkflowEventPublisher(
    IDurableEventOutbox outbox,
    Zumbo.SharedKernel.IClock clock) : IPrivacyWorkflowEventPublisher
{
    public Task PublishAsync(PrivacyWorkflowDueEvent message, CancellationToken ct)
    {
        var correlationId = $"privacy-workflow:{message.JobId}:{message.DispatchSequence}";
        return outbox.EnqueueAsync(
            DurableEventEnvelope.Create(
                "Identity",
                PrivacyWorkflowEventTypes.Process,
                1,
                message.OrganizationId,
                correlationId,
                JsonSerializer.Serialize(message),
                clock.UtcNow,
                Hash(correlationId)),
            ct);
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

public sealed class PrivacyWorkflowDurableHandler(
    PrivacyWorkflowProcessor processor,
    IUserRepository users,
    IHttpContextAccessor httpContextAccessor) : IDurableEventHandler
{
    public string ConsumerName => "identity-privacy-workflow-v1";
    public string EventType => PrivacyWorkflowEventTypes.Process;

    public async Task HandleAsync(
        DurableEventEnvelope message,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<PrivacyWorkflowDueEvent>(message.Payload)
            ?? throw new InvalidOperationException("Privacy workflow payload is invalid.");
        var actor = await users.GetByIdAsync(payload.RequestedByUserId, cancellationToken);
        var roles = actor is { IsActive: true } ? actor.Roles : [];
        var previous = httpContextAccessor.HttpContext;
        var context = new DefaultHttpContext();
        context.User = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
            [
                new(System.Security.Claims.ClaimTypes.NameIdentifier, payload.RequestedByUserId),
                new("organizationId", payload.OrganizationId),
                .. roles.Select(role => new System.Security.Claims.Claim(
                    System.Security.Claims.ClaimTypes.Role,
                    role))
            ], "PrivacyWorkflow"));
        context.TraceIdentifier = message.CorrelationId;
        httpContextAccessor.HttpContext = context;
        try
        {
            await processor.ProcessAsync(payload, cancellationToken);
        }
        finally
        {
            httpContextAccessor.HttpContext = previous;
        }
    }
}
