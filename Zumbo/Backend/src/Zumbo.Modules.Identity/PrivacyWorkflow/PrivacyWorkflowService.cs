using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Identity;

public sealed class PrivacyWorkflowService(
    IDocumentRepository<PrivacyWorkflowDocument> jobs,
    PrivacyService privacy,
    IPrivacyWorkflowEventPublisher publisher,
    IDurableTransactionRunner transactions,
    IOptions<PrivacyWorkflowOptions> configuredOptions,
    IClock clock,
    ICurrentUser currentUser)
{
    private PrivacyWorkflowOptions Options => configuredOptions.Value;

    public async Task<PrivacyWorkflowReceipt> SubmitAnonymizationAsync(
        AnonymizeAccountRequest request,
        CancellationToken ct)
    {
        var context = await privacy.ValidateAnonymizationAsync(request, ct);
        var existing = await jobs.SelectAsync(x =>
            x.OrganizationId == context.OrganizationId
            && x.RequestedByUserId == context.UserId
            && (x.State == PrivacyWorkflowStates.Pending
                || x.State == PrivacyWorkflowStates.Running
                || x.State == PrivacyWorkflowStates.Failed), ct);
        if (existing is not null)
        {
            throw new ConflictException(
                "PRIVACY_WORKFLOW_EXISTS",
                "An active privacy workflow already exists for this account.");
        }

        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        var now = clock.UtcNow;
        var expiresAt = now.AddDays(Options.RetentionDays);
        var job = new PrivacyWorkflowDocument
        {
            Id = Hash($"privacy-workflow\u001f{context.OrganizationId}\u001f{context.UserId}")[..32],
            OrganizationId = context.OrganizationId,
            RequestedByUserId = context.UserId,
            Pseudonym = context.Pseudonym,
            StatusTokenHash = Hash(rawToken),
            DispatchSequence = 1,
            CreatedAt = now,
            UpdatedAt = now,
            ExpiresAt = expiresAt,
            ExpiresAtUtc = expiresAt.UtcDateTime
        };
        try
        {
            await transactions.ExecuteAsync(
                "Identity",
                async token =>
                {
                    await jobs.CreateAsync(job, token);
                    await publisher.PublishAsync(ToEvent(job), token);
                },
                ct);
        }
        catch (DocumentConflictException)
        {
            throw new ConflictException(
                "PRIVACY_WORKFLOW_EXISTS",
                "An active privacy workflow already exists for this account.");
        }
        return new PrivacyWorkflowReceipt(ToResponse(job), rawToken);
    }

    public async Task<PrivacyWorkflowResponse> GetAsync(string jobId, CancellationToken ct) =>
        ToResponse(await GetOwnedAsync(jobId, ct));

    public async Task<PrivacyWorkflowPublicStatus> GetPublicStatusAsync(
        string jobId,
        string statusToken,
        CancellationToken ct)
    {
        var job = await GetByTokenAsync(jobId, statusToken, ct);
        return new PrivacyWorkflowPublicStatus(
            job.Id,
            job.State,
            Progress(job),
            job.UpdatedAt,
            job.ExpiresAt,
            job.CompletedAt);
    }

    public async Task<PrivacyWorkflowPublicStatus> RecoverWithTokenAsync(
        string jobId,
        string statusToken,
        CancellationToken ct)
    {
        var job = await GetByTokenAsync(jobId, statusToken, ct);
        var staleBefore = clock.UtcNow.AddSeconds(-Options.LeaseSeconds);
        if (job.State != PrivacyWorkflowStates.Failed
            && !(job.State == PrivacyWorkflowStates.Running && job.UpdatedAt <= staleBefore))
        {
            throw new ConflictException(
                "PRIVACY_WORKFLOW_NOT_RECOVERABLE",
                "Privacy workflow is not failed or stale.");
        }
        await RedispatchAsync(job, ct);
        return new PrivacyWorkflowPublicStatus(
            job.Id,
            job.State,
            Progress(job),
            job.UpdatedAt,
            job.ExpiresAt,
            job.CompletedAt);
    }

    public async Task<PrivacyRetentionResult> PurgeWithTokenAsync(
        string jobId,
        string statusToken,
        CancellationToken ct)
    {
        var job = await LoadByTokenAsync(jobId, statusToken, ct);
        if (job.ExpiresAt > clock.UtcNow)
        {
            throw new ConflictException(
                "PRIVACY_WORKFLOW_RETENTION_ACTIVE",
                "Privacy workflow retention has not expired.");
        }
        var deleted = await jobs.DeleteByFilterAsync(x =>
            x.Id == job.Id
            && x.OrganizationId == job.OrganizationId
            && x.StatusTokenHash == job.StatusTokenHash,
            ct);
        return new PrivacyRetentionResult(checked((int)deleted));
    }

    public async Task<PrivacyWorkflowResponse> RetryAsync(string jobId, CancellationToken ct)
    {
        var job = await GetOwnedAsync(jobId, ct);
        if (job.State != PrivacyWorkflowStates.Failed)
        {
            throw new ConflictException(
                "PRIVACY_WORKFLOW_NOT_RETRYABLE",
                "Only failed privacy workflows can be retried.");
        }
        await RedispatchAsync(job, ct);
        return ToResponse(job);
    }

    public async Task<PrivacyWorkflowResponse> ReconcileAsync(string jobId, CancellationToken ct)
    {
        var job = await GetOwnedAsync(jobId, ct);
        var staleBefore = clock.UtcNow.AddSeconds(-Options.LeaseSeconds);
        if (job.State == PrivacyWorkflowStates.Running && job.UpdatedAt > staleBefore)
        {
            throw new ConflictException(
                "PRIVACY_WORKFLOW_LEASE_ACTIVE",
                "Privacy workflow is still making progress.");
        }
        if (job.State is PrivacyWorkflowStates.Completed or PrivacyWorkflowStates.Expired)
        {
            throw new ConflictException(
                "PRIVACY_WORKFLOW_TERMINAL",
                "A terminal privacy workflow cannot be reconciled.");
        }
        await RedispatchAsync(job, ct);
        return ToResponse(job);
    }

    public async Task<PrivacyRetentionResult> PurgeExpiredAsync(CancellationToken ct)
    {
        var userId = RequireUser();
        var organizationId = currentUser.OrganizationId
            ?? throw new UnauthorizedException("Authenticated organization is required.");
        var deleted = await jobs.DeleteByFilterAsync(x =>
            x.OrganizationId == organizationId
            && x.RequestedByUserId == userId
            && x.ExpiresAtUtc <= clock.UtcNow.UtcDateTime,
            ct);
        return new PrivacyRetentionResult(checked((int)deleted));
    }

    private async Task RedispatchAsync(PrivacyWorkflowDocument job, CancellationToken ct)
    {
        job.State = PrivacyWorkflowStates.Pending;
        job.LastErrorCode = null;
        job.LastErrorMessage = null;
        job.DispatchSequence++;
        job.UpdatedAt = clock.UtcNow;
        await transactions.ExecuteAsync(
            "Identity",
            async token =>
            {
                await ReplaceAsync(job, token);
                await publisher.PublishAsync(ToEvent(job), token);
            },
            ct);
    }

    private async Task<PrivacyWorkflowDocument> GetOwnedAsync(string jobId, CancellationToken ct)
    {
        var job = await jobs.SelectAsync(x => x.Id == jobId, ct);
        if (job is null
            || job.RequestedByUserId != RequireUser()
            || job.OrganizationId != currentUser.OrganizationId)
        {
            throw new NotFoundException(
                "PRIVACY_WORKFLOW_NOT_FOUND",
                "Privacy workflow was not found.");
        }
        await EnsureNotExpiredAsync(job, ct);
        return job;
    }

    private async Task<PrivacyWorkflowDocument> GetByTokenAsync(
        string jobId,
        string statusToken,
        CancellationToken ct)
    {
        var job = await LoadByTokenAsync(jobId, statusToken, ct);
        await EnsureNotExpiredAsync(job, ct);
        return job;
    }

    private async Task<PrivacyWorkflowDocument> LoadByTokenAsync(
        string jobId,
        string statusToken,
        CancellationToken ct)
    {
        var job = await jobs.SelectAsync(x => x.Id == jobId, ct);
        if (job is null || string.IsNullOrWhiteSpace(statusToken)
            || !FixedTimeEquals(job.StatusTokenHash, Hash(statusToken)))
        {
            throw new NotFoundException(
                "PRIVACY_WORKFLOW_NOT_FOUND",
                "Privacy workflow was not found.");
        }
        return job;
    }

    private Task EnsureNotExpiredAsync(PrivacyWorkflowDocument job, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (job.ExpiresAt > clock.UtcNow) return Task.CompletedTask;
        throw new NotFoundException(
            "PRIVACY_WORKFLOW_EXPIRED",
            "Privacy workflow retention has expired.");
    }

    private async Task ReplaceAsync(PrivacyWorkflowDocument job, CancellationToken ct)
    {
        var result = await jobs.ReplaceByVersionAsync(
            x => x.Id == job.Id && x.OrganizationId == job.OrganizationId,
            job,
            job.Version,
            ct);
        if (!result.Found)
        {
            throw new ConflictException(
                "PRIVACY_WORKFLOW_CONFLICT",
                "Privacy workflow changed concurrently; reload and retry.");
        }
        job.Version = result.Version!.Value;
    }

    private string RequireUser() => currentUser.UserId
        ?? throw new UnauthorizedException("Authenticated user is required.");

    internal static PrivacyWorkflowDueEvent ToEvent(PrivacyWorkflowDocument job) =>
        new(job.OrganizationId, job.RequestedByUserId, job.Id, job.DispatchSequence);

    internal static PrivacyWorkflowResponse ToResponse(PrivacyWorkflowDocument job) =>
        new(
            job.Id,
            job.State,
            job.CompletedSteps,
            job.TotalSteps,
            Progress(job),
            job.Attempts,
            job.LastErrorCode,
            job.LastErrorMessage,
            job.CreatedAt,
            job.UpdatedAt,
            job.ExpiresAt,
            job.CompletedAt,
            job.Version);

    private static int Progress(PrivacyWorkflowDocument job) =>
        job.TotalSteps <= 0 ? 0 : Math.Clamp(job.CompletedSteps * 100 / job.TotalSteps, 0, 100);

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static bool FixedTimeEquals(string left, string right)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(left),
                Convert.FromHexString(right));
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
