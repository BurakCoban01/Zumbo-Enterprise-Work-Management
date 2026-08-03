using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.Modules.Identity;

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
