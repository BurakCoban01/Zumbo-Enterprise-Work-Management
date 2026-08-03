using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemWebhookService{

    private Task WriteAuditAsync(
        string action,
        string entityType,
        string entityId,
        string? oldValue,
        string? newValue,
        string? correlationId,
        CancellationToken ct) =>
        audit.WriteAsync(
            action,
            entityType,
            entityId,
            oldValue,
            newValue,
            string.IsNullOrWhiteSpace(correlationId) ? Guid.NewGuid().ToString("N") : correlationId,
            ct);
}
