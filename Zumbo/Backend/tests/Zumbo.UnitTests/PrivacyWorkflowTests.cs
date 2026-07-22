using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.BuildingBlocks.Infrastructure.Concurrency;
using Zumbo.BuildingBlocks.Infrastructure.Messaging;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.BuildingBlocks.Infrastructure.Security;
using Zumbo.Modules.Identity;
using Zumbo.SharedKernel;

namespace Zumbo.UnitTests;

public sealed class PrivacyWorkflowTests
{
    [Fact]
    public async Task Workflow_ResumesFromCheckpointAndTokenRecoversAfterCredentialsAreRevoked()
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 7, 20, 12, 0, 0, TimeSpan.Zero));
        var currentUser = new MutableCurrentUser { UserId = "privacy-user", OrganizationId = "org-1" };
        var hasher = new Pbkdf2PasswordHasher();
        var userDocuments = new InMemoryDocumentRepository<UserDocument>();
        var users = new UserRepository(userDocuments);
        await users.AddAsync(new UserDocument
        {
            Id = "privacy-user",
            Username = "privacy-user",
            Email = "privacy-user@zumbo.local",
            OrganizationId = "org-1",
            PasswordHash = hasher.Hash("P@ssword123"),
            CreatedAt = clock.UtcNow
        }, CancellationToken.None);
        var dataProcessor = new RecordingPrivacyProcessor();
        var audit = new FailingOnceAuditWriter();
        var privacy = new PrivacyService(
            users,
            new RefreshSessionStore(new InMemoryDocumentRepository<RefreshSessionDocument>()),
            new ApiKeyStore(new InMemoryDocumentRepository<ApiKeyDocument>()),
            new InMemoryDurableTransactionRunner(),
            hasher,
            dataProcessor,
            audit,
            new InMemoryDistributedLockProvider(),
            Options.Create(new DistributedLockOptions()),
            clock,
            currentUser);
        var jobs = new InMemoryDocumentRepository<PrivacyWorkflowDocument>();
        var publisher = new RecordingPublisher();
        var options = Options.Create(new PrivacyWorkflowOptions
        {
            RetentionDays = 7,
            LeaseSeconds = 30
        });
        var service = new PrivacyWorkflowService(
            jobs,
            privacy,
            publisher,
            new InMemoryDurableTransactionRunner(),
            options,
            clock,
            currentUser);
        var processor = new PrivacyWorkflowProcessor(
            jobs,
            privacy,
            new InMemoryDistributedLockProvider(),
            Options.Create(new DistributedLockOptions()),
            options,
            clock,
            currentUser);

        var receipt = await service.SubmitAnonymizationAsync(
            new AnonymizeAccountRequest("P@ssword123", "ANONYMIZE"),
            CancellationToken.None);
        await Assert.ThrowsAsync<ConflictException>(() => service.SubmitAnonymizationAsync(
            new AnonymizeAccountRequest("P@ssword123", "ANONYMIZE"),
            CancellationToken.None));
        var reconciled = await service.ReconcileAsync(receipt.Job.Id, CancellationToken.None);
        Assert.Equal(PrivacyWorkflowStates.Pending, reconciled.State);
        await processor.ProcessAsync(publisher.Events[0], CancellationToken.None);
        Assert.Equal(0, dataProcessor.AnonymizationCalls);
        await Task.WhenAll(
            processor.ProcessAsync(publisher.Events[1], CancellationToken.None),
            processor.ProcessAsync(publisher.Events[1], CancellationToken.None));

        var failed = await jobs.SelectAsync(x => x.Id == receipt.Job.Id, CancellationToken.None);
        Assert.NotNull(failed);
        Assert.Equal(PrivacyWorkflowStates.Failed, failed!.State);
        Assert.Equal(1, failed.CompletedSteps);
        Assert.Equal(1, dataProcessor.AnonymizationCalls);
        Assert.False((await users.GetByIdAsync("privacy-user", CancellationToken.None))!.IsActive);

        var recovered = await service.RecoverWithTokenAsync(
            failed.Id,
            receipt.StatusToken,
            CancellationToken.None);
        Assert.Equal(PrivacyWorkflowStates.Pending, recovered.State);
        await processor.ProcessAsync(publisher.Events.Last(), CancellationToken.None);
        await processor.ProcessAsync(publisher.Events.Last(), CancellationToken.None);

        var completed = await jobs.SelectAsync(x => x.Id == receipt.Job.Id, CancellationToken.None);
        Assert.Equal(PrivacyWorkflowStates.Completed, completed!.State);
        Assert.Equal(2, completed.CompletedSteps);
        Assert.Equal(1, dataProcessor.AnonymizationCalls);
        Assert.Equal(2, audit.Attempts);
        var status = await service.GetPublicStatusAsync(
            completed.Id,
            receipt.StatusToken,
            CancellationToken.None);
        Assert.Equal(100, status.ProgressPercent);
        await Assert.ThrowsAsync<ConflictException>(() => service.PurgeWithTokenAsync(
            completed.Id,
            receipt.StatusToken,
            CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetPublicStatusAsync(
            completed.Id,
            "wrong-token",
            CancellationToken.None));

        currentUser.UserId = "foreign-user";
        currentUser.OrganizationId = "org-2";
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync(
            completed.Id,
            CancellationToken.None));

        currentUser.UserId = "privacy-user";
        currentUser.OrganizationId = "org-1";
        clock.UtcNow = clock.UtcNow.AddDays(8);
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetPublicStatusAsync(
            completed.Id,
            receipt.StatusToken,
            CancellationToken.None));
        Assert.Equal(1, (await service.PurgeWithTokenAsync(
            completed.Id,
            receipt.StatusToken,
            CancellationToken.None)).Deleted);
    }

    private sealed class RecordingPrivacyProcessor : IPrivacyDataProcessor
    {
        public int AnonymizationCalls { get; private set; }

        public Task<IReadOnlyCollection<PrivacyDataGroup>> ExportAsync(
            string userId,
            string organizationId,
            CancellationToken ct) =>
            Task.FromResult<IReadOnlyCollection<PrivacyDataGroup>>([]);

        public async Task<long> WriteExportAsync(
            string userId,
            string organizationId,
            UserProfileResponse profile,
            Stream destination,
            CancellationToken ct)
        {
            await destination.WriteAsync("{}\n"u8.ToArray(), ct);
            return 1;
        }

        public Task EnsureCanAnonymizeAsync(
            string userId,
            string organizationId,
            CancellationToken ct) => Task.CompletedTask;

        public Task AnonymizeReferencesAsync(
            string userId,
            string organizationId,
            string pseudonym,
            string username,
            string email,
            CancellationToken ct)
        {
            AnonymizationCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class FailingOnceAuditWriter : IIdentityAuditWriter
    {
        public int Attempts { get; private set; }

        public Task WriteAsync(
            string action,
            string entityId,
            string? oldValue,
            string? newValue,
            string correlationId,
            CancellationToken ct)
        {
            Attempts++;
            if (Attempts == 1) throw new InvalidOperationException("Synthetic audit outage.");
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingPublisher : IPrivacyWorkflowEventPublisher
    {
        public List<PrivacyWorkflowDueEvent> Events { get; } = [];

        public Task PublishAsync(PrivacyWorkflowDueEvent message, CancellationToken ct)
        {
            Events.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class MutableClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = now;
    }

    private sealed class MutableCurrentUser : ICurrentUser
    {
        public string? UserId { get; set; }
        public string? OrganizationId { get; set; }
        public IReadOnlyCollection<string> Roles { get; set; } = ["User"];
    }
}
