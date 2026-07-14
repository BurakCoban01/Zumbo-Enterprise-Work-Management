using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Notifications;
using Zumbo.Modules.WorkItems;

public sealed class NotificationUserDirectoryAdapter(IUserRepository users) : INotificationUserDirectory
{
    public async Task<NotificationUser?> FindAsync(string userId, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(userId, ct);
        return user is null ? null : new NotificationUser(user.Id, user.Email, user.IsActive);
    }
}

public sealed class SmtpEmailNotificationSender(IOptions<EmailNotificationOptions> options) : IEmailNotificationSender
{
    public async Task SendAsync(string recipient, string subject, string body, CancellationToken ct)
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
}

public sealed class NotificationEmailDispatcherHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<EmailNotificationOptions> options,
    ILogger<NotificationEmailDispatcherHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<NotificationService>();
                await service.DispatchPendingEmailsAsync(50, stoppingToken);
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

public sealed class DueDateReminderHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<DueDateReminderOptions> options,
    ILogger<DueDateReminderHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Clamp(options.Value.IntervalMinutes, 1, 1440));
        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<WorkItemService>();
                await service.SendDueDateRemindersAsync(options.Value.HorizonHours, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Due-date reminder dispatcher iteration failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
