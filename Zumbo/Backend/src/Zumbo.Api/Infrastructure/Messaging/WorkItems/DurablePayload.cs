using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Search;
using Zumbo.Modules.Audit;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Notifications;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

internal static class DurablePayload
{
    internal static T Read<T>(DurableEventEnvelope message) =>
        JsonSerializer.Deserialize<T>(message.Payload)
        ?? throw new InvalidOperationException($"Durable event '{message.Id}' contains an invalid {typeof(T).Name} payload.");
}
