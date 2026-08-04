using Microsoft.Extensions.Options;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Notifications;

public sealed class PasswordResetNotifierAdapter(
    IEmailNotificationSender emailSender,
    IOptions<EmailNotificationOptions> emailOptions,
    IOptions<PasswordResetOptions> resetOptions,
    ILogger<PasswordResetNotifierAdapter> logger) : IPasswordResetNotifier
{
    public async Task SendAsync(string email, string rawToken, DateTimeOffset expiresAt, CancellationToken ct)
    {
        if (!emailOptions.Value.Enabled)
        {
            logger.LogWarning("Password reset email delivery is disabled for recipient domain {RecipientDomain}", Domain(email));
            return;
        }

        var baseUrl = resetOptions.Value.FrontendResetUrl.Trim();
        var separator = baseUrl.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        var resetUrl = $"{baseUrl}{separator}token={Uri.EscapeDataString(rawToken)}";
        try
        {
            await emailSender.SendAsync(
                email,
                "Zumbo password reset",
                $"Use this single-use link before {expiresAt:O}: {resetUrl}",
                ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Password reset email delivery failed for recipient domain {RecipientDomain}", Domain(email));
        }
    }

    private static string Domain(string email) =>
        email[(email.LastIndexOf('@') + 1)..];
}
