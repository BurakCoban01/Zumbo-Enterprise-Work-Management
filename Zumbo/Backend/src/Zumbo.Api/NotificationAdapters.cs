using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Runtime;
using Zumbo.Modules.Audit;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Notifications;

public sealed class NotificationUserDirectoryAdapter(IUserRepository users) : INotificationUserDirectory
{
    public async Task<NotificationUser?> FindAsync(string userId, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(userId, ct);
        return user is null ? null : new NotificationUser(user.Id, user.OrganizationId, user.Email, user.IsActive);
    }
}

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

public sealed class SmtpEmailNotificationSender : IEmailNotificationSender
{
    private readonly IOptions<EmailNotificationOptions> options;
    private readonly IExternalDependencyPolicy? resiliencePolicy;

    public SmtpEmailNotificationSender(IOptions<EmailNotificationOptions> options)
        : this(options, null)
    {
    }

    public SmtpEmailNotificationSender(
        IOptions<EmailNotificationOptions> options,
        IExternalDependencyPolicyProvider? policyProvider)
    {
        this.options = options;
        resiliencePolicy = policyProvider?.Get(ExternalDependencyNames.Smtp);
    }

    public async Task SendAsync(string recipient, string subject, string body, CancellationToken ct)
    {
        if (resiliencePolicy is null)
        {
            await SendCoreAsync(recipient, subject, body, ct);
            return;
        }

        await resiliencePolicy.ExecuteAsync(
            "send",
            ExternalDependencyOperationKind.NonIdempotentWrite,
            token => SendCoreAsync(recipient, subject, body, token),
            IsTransient,
            ct);
    }

    private async Task SendCoreAsync(string recipient, string subject, string body, CancellationToken ct)
    {
        var configuration = options.Value;
        if (!configuration.Enabled)
        {
            throw new InvalidOperationException("Email notification delivery is disabled.");
        }

        using var message = new MailMessage
        {
            From = new MailAddress(configuration.FromAddress, configuration.FromName),
            Subject = subject,
            Body = body,
            IsBodyHtml = false
        };
        message.To.Add(new MailAddress(recipient));
        using var client = new SmtpClient(configuration.Host, configuration.Port)
        {
            EnableSsl = configuration.UseSsl,
            Credentials = string.IsNullOrWhiteSpace(configuration.Username)
                ? CredentialCache.DefaultNetworkCredentials
                : new NetworkCredential(configuration.Username, configuration.Password)
        };
        await client.SendMailAsync(message, ct);
    }

    private static bool IsTransient(Exception exception) =>
        exception is SmtpException or SocketException or IOException;
}

public sealed class NotificationEmailDispatcherHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<EmailNotificationOptions> options,
    ILogger<NotificationEmailDispatcherHostedService> logger) : BackgroundService
{
    private readonly string workerId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(
            Math.Clamp(options.Value.DispatcherIntervalSeconds, 1, 3600)));
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<NotificationService>();
                await service.DispatchPendingEmailsAsync(
                    Math.Clamp(options.Value.DispatchBatchSize, 1, 100),
                    stoppingToken,
                    workerId);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Notification email dispatcher iteration failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
