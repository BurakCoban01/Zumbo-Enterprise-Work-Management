using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Runtime;
using Zumbo.Modules.Audit;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Notifications;

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
