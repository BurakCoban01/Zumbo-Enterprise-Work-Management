using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Runtime;
using Zumbo.Modules.Notifications;
using Zumbo.Modules.WorkItems;

namespace Zumbo.ApiTests;

public sealed class ExternalDependencyAdapterTests
{
    [Fact]
    public async Task SmtpSend_IsClassifiedAsNonIdempotentAndNotInvokedAfterCircuitRejection()
    {
        var policy = new RejectingPolicy(new ExternalDependencyCircuitOpenException("smtp"));
        var sender = new SmtpEmailNotificationSender(
            Options.Create(new EmailNotificationOptions { Enabled = true }),
            new StubPolicyProvider(policy));

        await Assert.ThrowsAsync<ExternalDependencyCircuitOpenException>(() => sender.SendAsync(
            "recipient@example.test", "subject", "body", default));

        Assert.Equal(ExternalDependencyOperationKind.NonIdempotentWrite, policy.Kind);
        Assert.False(policy.ActionInvoked);
    }

    [Fact]
    public async Task WebhookPolicyTimeout_UsesSafeCodeAndNeverLeaksDependencyDetails()
    {
        var policy = new RejectingPolicy(new ExternalDependencyTimeoutException("webhook", "send"));
        var options = Options.Create(new WebhookOptions());
        var sender = new PinnedWebhookSender(
            new WebhookTargetPolicy(options),
            options,
            new StubPolicyProvider(policy));

        var exception = await Assert.ThrowsAsync<WebhookDeliveryException>(() => sender.SendAsync(
            new WebhookSendRequest(
                "https://receiver.example.test/hooks",
                "{\"secret\":\"must-not-leak\"}",
                "delivery-1",
                1,
                1,
                "signature",
                null,
                null),
            default));

        Assert.Equal("REQUEST_TIMEOUT", exception.SafeCode);
        Assert.Equal(ExternalDependencyOperationKind.NonIdempotentWrite, policy.Kind);
        Assert.False(policy.ActionInvoked);
        Assert.DoesNotContain("secret", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OpenCircuit_IsReportedAsDegradedWithoutFailingReadiness()
    {
        var provider = new StubPolicyProvider(
            new RejectingPolicy(new InvalidOperationException()),
            [new ExternalDependencySnapshot(
                ExternalDependencyNames.OpenSearch,
                3, 3, 0, 0, 3, 0, 1, 0, 0, 0, true, 12)]);
        var check = new ExternalDependencyPolicyHealthCheck(provider);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains(ExternalDependencyNames.OpenSearch, result.Description);
        Assert.Equal(7, result.Data.Count);
    }

    private sealed class StubPolicyProvider(
        IExternalDependencyPolicy policy,
        IReadOnlyList<ExternalDependencySnapshot>? snapshots = null) : IExternalDependencyPolicyProvider
    {
        public IExternalDependencyPolicy Get(string dependency) => policy;
        public IReadOnlyList<ExternalDependencySnapshot> GetSnapshots() => snapshots ?? [];
    }

    private sealed class RejectingPolicy(Exception exception) : IExternalDependencyPolicy
    {
        public ExternalDependencyOperationKind? Kind { get; private set; }
        public bool ActionInvoked { get; private set; }

        public Task<T> ExecuteAsync<T>(
            string operation,
            ExternalDependencyOperationKind operationKind,
            Func<CancellationToken, Task<T>> action,
            Func<Exception, bool>? isTransient = null,
            CancellationToken cancellationToken = default)
        {
            Kind = operationKind;
            return Task.FromException<T>(exception);
        }

        public Task ExecuteAsync(
            string operation,
            ExternalDependencyOperationKind operationKind,
            Func<CancellationToken, Task> action,
            Func<Exception, bool>? isTransient = null,
            CancellationToken cancellationToken = default)
        {
            Kind = operationKind;
            return Task.FromException(exception);
        }
    }
}
