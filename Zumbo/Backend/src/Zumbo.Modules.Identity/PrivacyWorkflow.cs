using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Identity;

public sealed class PrivacyWorkflowOptions
{
    public int RetentionDays { get; init; } = 7;
    public int LeaseSeconds { get; init; } = 60;
}

public static class PrivacyWorkflowStates
{
    public const string Pending = "Pending";
    public const string Running = "Running";
    public const string Failed = "Failed";
    public const string Completed = "Completed";
    public const string Expired = "Expired";
}

public sealed class PrivacyWorkflowDocument : IVersionedDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OrganizationId { get; set; } = string.Empty;
    public string RequestedByUserId { get; set; } = string.Empty;
    public string State { get; set; } = PrivacyWorkflowStates.Pending;
    public string Pseudonym { get; set; } = string.Empty;
    public string StatusTokenHash { get; set; } = string.Empty;
    public int CompletedSteps { get; set; }
    public int TotalSteps { get; set; } = 2;
    public int DispatchSequence { get; set; }
    public int Attempts { get; set; }
    public string? LastErrorCode { get; set; }
    public string? LastErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public long Version { get; set; }
}

public sealed record PrivacyWorkflowDueEvent(
    string OrganizationId,
    string RequestedByUserId,
    string JobId,
    int DispatchSequence);

public interface IPrivacyWorkflowEventPublisher
{
    Task PublishAsync(PrivacyWorkflowDueEvent message, CancellationToken ct);
}

public sealed record PrivacyWorkflowResponse(
    string Id,
    string State,
    int CompletedSteps,
    int TotalSteps,
    int ProgressPercent,
    int Attempts,
    string? LastErrorCode,
    string? LastErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? CompletedAt,
    long Version);

public sealed record PrivacyWorkflowReceipt(
    PrivacyWorkflowResponse Job,
    string StatusToken);

public sealed record PrivacyWorkflowPublicStatus(
    string Id,
    string State,
    int ProgressPercent,
    DateTimeOffset UpdatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? CompletedAt);

public sealed record PrivacyRetentionResult(int Deleted);

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

public sealed class PrivacyWorkflowProcessor(
    IDocumentRepository<PrivacyWorkflowDocument> jobs,
    PrivacyService privacy,
    IDistributedLockProvider distributedLockProvider,
    IOptions<DistributedLockOptions> distributedLockOptions,
    IOptions<PrivacyWorkflowOptions> workflowOptions,
    IClock clock,
    ICurrentUser currentUser)
{
    public async Task ProcessAsync(PrivacyWorkflowDueEvent message, CancellationToken ct)
    {
        var lockOptions = distributedLockOptions.Value;
        await using var workflowLock = await distributedLockProvider.TryAcquireAsync(
            "privacy-workflow:" + message.JobId,
            TimeSpan.FromSeconds(Math.Clamp(workflowOptions.Value.LeaseSeconds, 30, 3600)),
            TimeSpan.FromSeconds(Math.Clamp(lockOptions.WaitSeconds, 0, 30)),
            ct)
            ?? throw new ConflictException(
                "PRIVACY_WORKFLOW_BUSY",
                "Privacy workflow is already being processed; retry delivery later.");
        var job = await jobs.SelectAsync(x => x.Id == message.JobId, ct);
        if (job is null
            || job.OrganizationId != message.OrganizationId
            || job.RequestedByUserId != message.RequestedByUserId)
        {
            throw new ConflictException(
                "PRIVACY_WORKFLOW_EVENT_INVALID",
                "Privacy workflow event ownership is invalid.");
        }
        if (currentUser.UserId != job.RequestedByUserId
            || currentUser.OrganizationId != job.OrganizationId)
        {
            throw new ConflictException(
                "PRIVACY_WORKFLOW_ACTOR_INVALID",
                "Privacy workflow actor context is invalid.");
        }
        if (job.DispatchSequence != message.DispatchSequence
            || job.State is PrivacyWorkflowStates.Failed
                or PrivacyWorkflowStates.Completed
                or PrivacyWorkflowStates.Expired)
        {
            return;
        }

        try
        {
            if (job.State == PrivacyWorkflowStates.Pending)
            {
                job.State = PrivacyWorkflowStates.Running;
                job.StartedAt ??= clock.UtcNow;
                job.Attempts++;
                job.UpdatedAt = clock.UtcNow;
                await ReplaceAsync(job, ct);
            }

            if (job.CompletedSteps == 0)
            {
                await privacy.AnonymizeReferencesForWorkflowAsync(
                    job.RequestedByUserId,
                    job.Pseudonym,
                    ct);
                job.CompletedSteps = 1;
                job.UpdatedAt = clock.UtcNow;
                await ReplaceAsync(job, ct);
            }

            if (job.CompletedSteps == 1)
            {
                await privacy.FinalizeAnonymizationForWorkflowAsync(
                    job.RequestedByUserId,
                    job.Pseudonym,
                    $"privacy-workflow:{job.Id}",
                    ct);
                job.CompletedSteps = 2;
                job.State = PrivacyWorkflowStates.Completed;
                job.CompletedAt = clock.UtcNow;
                job.UpdatedAt = clock.UtcNow;
                await ReplaceAsync(job, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (ConflictException exception) when (exception.Code == "PRIVACY_WORKFLOW_CONFLICT")
        {
            // Another delivery advanced the same checkpoint first.
            return;
        }
        catch (Exception exception)
        {
            var latest = await jobs.SelectAsync(x => x.Id == job.Id, ct) ?? job;
            latest.State = PrivacyWorkflowStates.Failed;
            latest.LastErrorCode = exception is ZumboException zumbo
                ? zumbo.Code
                : "PRIVACY_WORKFLOW_DEPENDENCY_FAILED";
            latest.LastErrorMessage = exception is ZumboException
                ? Limit(exception.Message)
                : "Privacy workflow dependency failed; retry or reconcile is available.";
            latest.UpdatedAt = clock.UtcNow;
            await ReplaceAsync(latest, ct);
        }
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
                "Privacy workflow changed concurrently.");
        }
        job.Version = result.Version!.Value;
    }

    private static string Limit(string value) => value.Length <= 500 ? value : value[..500];
}
