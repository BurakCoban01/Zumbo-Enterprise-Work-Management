using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.Modules.Identity;

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
