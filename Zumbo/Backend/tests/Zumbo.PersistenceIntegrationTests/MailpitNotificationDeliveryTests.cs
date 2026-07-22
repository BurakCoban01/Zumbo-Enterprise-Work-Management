using System.Net.Mail;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Zumbo.Modules.Notifications;

namespace Zumbo.PersistenceIntegrationTests;

public sealed class MailpitNotificationDeliveryTests
{
    [Fact]
    public async Task SmtpDependencyFailureThenRecoveryDeliversExactlyOneMessage()
    {
        var host = Environment.GetEnvironmentVariable("ZUMBO_TEST_SMTP_HOST")
            ?? throw new InvalidOperationException("ZUMBO_TEST_SMTP_HOST is required.");
        var port = int.Parse(Environment.GetEnvironmentVariable("ZUMBO_TEST_SMTP_PORT")
            ?? throw new InvalidOperationException("ZUMBO_TEST_SMTP_PORT is required."));
        var apiUrl = Environment.GetEnvironmentVariable("ZUMBO_TEST_MAILPIT_API_URL")
            ?? throw new InvalidOperationException("ZUMBO_TEST_MAILPIT_API_URL is required.");
        var recipient = $"platform003-{Guid.NewGuid():N}@zumbo.local";

        var unavailable = new SmtpEmailNotificationSender(Options.Create(new EmailNotificationOptions
        {
            Enabled = true,
            Host = host,
            Port = port + 1000,
            UseSsl = false
        }));
        await Assert.ThrowsAnyAsync<SmtpException>(() => unavailable.SendAsync(
            recipient, "Unavailable", "Must not be delivered", CancellationToken.None));

        var sender = new SmtpEmailNotificationSender(Options.Create(new EmailNotificationOptions
        {
            Enabled = true,
            Host = host,
            Port = port,
            UseSsl = false,
            FromAddress = "platform003@zumbo.local"
        }));
        await sender.SendAsync(recipient, "PLATFORM-003 recovery", "Recovered delivery", CancellationToken.None);

        using var client = new HttpClient { BaseAddress = new Uri(apiUrl) };
        for (var attempt = 0; attempt < 20; attempt++)
        {
            using var response = await client.GetAsync("/api/v1/messages");
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var matches = json.RootElement.GetProperty("messages").EnumerateArray().Count(message =>
                message.GetProperty("To").EnumerateArray().Any(address =>
                    address.GetProperty("Address").GetString() == recipient));
            if (matches == 1) return;
            await Task.Delay(100);
        }

        throw new Xunit.Sdk.XunitException("Recovered SMTP message was not observed exactly once in Mailpit.");
    }
}
