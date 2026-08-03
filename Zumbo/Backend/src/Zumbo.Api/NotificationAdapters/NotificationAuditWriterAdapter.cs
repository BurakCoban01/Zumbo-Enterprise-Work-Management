using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Runtime;
using Zumbo.Modules.Audit;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Notifications;

public sealed class NotificationAuditWriterAdapter(AuditService audit) : INotificationAuditWriter
{
    public Task WriteAsync(
        string action,
        string entityId,
        string? oldValue,
        string? newValue,
        string correlationId,
        CancellationToken ct) =>
        audit.WriteAsync(
            action,
            "Notification",
            entityId,
            oldValue,
            newValue,
            correlationId,
            ct);
}
