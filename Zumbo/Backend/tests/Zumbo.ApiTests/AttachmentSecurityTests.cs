using System.IO.Compression;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.BuildingBlocks.Infrastructure.Storage;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.ApiTests;

public sealed class AttachmentSecurityTests
{
    [Fact]
    public async Task Adapter_RequiresMatchingExtensionMimeAndCompleteSignature()
    {
        await using var fixture = new AttachmentFixture(AttachmentMalwareScanStatuses.Clean);

        var stored = await fixture.Adapter.SaveAsync(
            new MemoryStream("valid UTF-8 text"u8.ToArray()),
            "../notes.txt",
            "text/plain; charset=utf-8",
            1024,
            default);

        Assert.Equal("notes.txt", stored.FileName);
        Assert.Equal("text/plain", stored.ContentType);
        Assert.Equal(AttachmentSecurityStates.Clean, stored.SecurityState);
        Assert.StartsWith("attachments/", stored.StoragePath, StringComparison.Ordinal);
        Assert.DoesNotContain("..", stored.StoragePath, StringComparison.Ordinal);

        await Assert.ThrowsAsync<ValidationException>(() => fixture.Adapter.SaveAsync(
            new MemoryStream("not png"u8.ToArray()),
            "spoofed.png",
            "image/png",
            1024,
            default));
        await Assert.ThrowsAsync<ValidationException>(() => fixture.Adapter.SaveAsync(
            new MemoryStream("text"u8.ToArray()),
            "wrong.png",
            "text/plain",
            1024,
            default));
    }

    [Fact]
    public async Task Adapter_PreBuffersActualSizeAndVerifiesStoredChecksum()
    {
        await using var fixture = new AttachmentFixture(AttachmentMalwareScanStatuses.Clean);
        await Assert.ThrowsAsync<ValidationException>(() => fixture.Adapter.SaveAsync(
            new NonSeekableReadStream(new byte[128]),
            "underreported.txt",
            "text/plain",
            16,
            default));

        var stored = await fixture.Adapter.SaveAsync(
            new MemoryStream("checksum contract"u8.ToArray()),
            "checksum.txt",
            "text/plain",
            1024,
            default);
        await File.WriteAllTextAsync(Path.Combine(fixture.RootPath, stored.StoragePath), "tampered content");

        var exception = await Assert.ThrowsAsync<ConflictException>(() => fixture.Adapter.OpenReadAsync(
            stored.StoragePath,
            stored.ContentType,
            stored.ChecksumSha256,
            default));
        Assert.Equal("ATTACHMENT_INTEGRITY_FAILED", exception.Code);
    }

    [Fact]
    public async Task Adapter_RejectsArchiveBombOfficeMacroAndPolyglotSuffix()
    {
        await using var fixture = new AttachmentFixture(
            AttachmentMalwareScanStatuses.Clean,
            new AttachmentSecurityOptions { MaxArchiveCompressionRatio = 2 });

        var bomb = CreateZip(("payload.txt", new byte[2 * 1024 * 1024]));
        await Assert.ThrowsAsync<ValidationException>(() => fixture.Adapter.SaveAsync(
            new MemoryStream(bomb), "bomb.zip", "application/zip", bomb.Length, default));

        var macro = CreateZip(
            ("[Content_Types].xml", "<Types/>"u8.ToArray()),
            ("word/document.xml", "<document/>"u8.ToArray()),
            ("word/vbaProject.bin", "macro"u8.ToArray()));
        await Assert.ThrowsAsync<ValidationException>(() => fixture.Adapter.SaveAsync(
            new MemoryStream(macro),
            "macro.docx",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            macro.Length,
            default));

        var polyglot = CreateZip(("safe.txt", "safe"u8.ToArray())).Concat("<script>"u8.ToArray()).ToArray();
        await Assert.ThrowsAsync<ValidationException>(() => fixture.Adapter.SaveAsync(
            new MemoryStream(polyglot), "polyglot.zip", "application/zip", polyglot.Length, default));
    }

    [Fact]
    public async Task ScannerUnavailable_QuarantinesAndSuccessfulRetryPromotes()
    {
        await using var fixture = new AttachmentFixture(AttachmentMalwareScanStatuses.Unavailable);
        var quarantined = await fixture.Adapter.SaveAsync(
            new MemoryStream("scan later"u8.ToArray()),
            "scan.txt",
            "text/plain",
            1024,
            default);

        Assert.Equal(AttachmentSecurityStates.Quarantined, quarantined.SecurityState);
        Assert.StartsWith("quarantine/", quarantined.StoragePath, StringComparison.Ordinal);
        fixture.Scanner.Status = AttachmentMalwareScanStatuses.Clean;
        var clean = await fixture.Adapter.ReprocessAsync(quarantined, default);

        Assert.Equal(AttachmentSecurityStates.Clean, clean.SecurityState);
        Assert.StartsWith("attachments/", clean.StoragePath, StringComparison.Ordinal);
        await Assert.ThrowsAsync<FileNotFoundException>(() => fixture.FileStorage.OpenReadAsync(
            quarantined.StoragePath,
            quarantined.ContentType));
    }

    [Fact]
    public async Task Maintenance_RetriesQuarantineAndPersistsCleanState()
    {
        await using var fixture = new AttachmentFixture(AttachmentMalwareScanStatuses.Unavailable);
        var stored = await fixture.Adapter.SaveAsync(
            new MemoryStream("maintenance retry"u8.ToArray()),
            "retry.txt",
            "text/plain",
            1024,
            default);
        var repository = new InMemoryDocumentRepository<WorkItemAttachmentActivityDocument>();
        var document = await repository.CreateAsync(ToDocument(stored, fixture.Clock.UtcNow));
        fixture.Scanner.Status = AttachmentMalwareScanStatuses.Clean;
        var maintenance = new AttachmentSecurityMaintenanceService(
            repository,
            fixture.Adapter,
            fixture.Options,
            fixture.Clock);

        var result = await maintenance.RunBatchAsync(default);
        var updated = await repository.SelectAsync(x => x.Id == document.Id);

        Assert.Equal(1, result.Retried);
        Assert.Equal(1, result.Cleaned);
        Assert.Equal(AttachmentSecurityStates.Clean, updated!.SecurityState);
        Assert.StartsWith("attachments/", updated.StoragePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Maintenance_PersistsUnavailableRetryWithoutPromotingQuarantine()
    {
        await using var fixture = new AttachmentFixture(AttachmentMalwareScanStatuses.Unavailable);
        var stored = await fixture.Adapter.SaveAsync(
            new MemoryStream("maintenance unavailable"u8.ToArray()),
            "unavailable.txt",
            "text/plain",
            1024,
            default);
        var repository = new InMemoryDocumentRepository<WorkItemAttachmentActivityDocument>();
        var document = await repository.CreateAsync(ToDocument(stored, fixture.Clock.UtcNow));
        var maintenance = new AttachmentSecurityMaintenanceService(
            repository,
            fixture.Adapter,
            fixture.Options,
            fixture.Clock);

        var result = await maintenance.RunBatchAsync(default);
        var updated = await repository.SelectAsync(x => x.Id == document.Id);

        Assert.Equal(1, result.Retried);
        Assert.Equal(AttachmentSecurityStates.Quarantined, updated!.SecurityState);
        Assert.Equal("test outcome", updated.ScanDetail);
        Assert.Equal(2, updated.Version);
        Assert.StartsWith("quarantine/", updated.StoragePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Maintenance_PurgesExpiredMetadataAndReconcilesOldOrphans()
    {
        var future = DateTimeOffset.UtcNow.AddDays(3);
        await using var fixture = new AttachmentFixture(
            AttachmentMalwareScanStatuses.Unavailable,
            clock: new FixedClock(future));
        var repository = new InMemoryDocumentRepository<WorkItemAttachmentActivityDocument>();
        var quarantined = await fixture.Adapter.SaveAsync(
            new MemoryStream("expired quarantine"u8.ToArray()),
            "expired.txt",
            "text/plain",
            1024,
            default);
        await repository.CreateAsync(ToDocument(quarantined, future.AddDays(-2)));
        await repository.CreateAsync(ToDocument(
            quarantined with
            {
                StoragePath = string.Empty,
                SecurityState = AttachmentSecurityStates.Rejected
            },
            future.AddDays(-40)));
        var orphan = await fixture.FileStorage.SaveAsync(
            new MemoryStream("orphan"u8.ToArray()),
            "orphan.txt",
            "text/plain",
            1024);
        var maintenance = new AttachmentSecurityMaintenanceService(
            repository,
            fixture.Adapter,
            fixture.Options,
            fixture.Clock);

        var result = await maintenance.RunBatchAsync(default);

        Assert.Equal(2, result.PurgedMetadata);
        Assert.Equal(1, result.DeletedOrphans);
        Assert.Equal(0, await repository.CountByFilterAsync());
        await Assert.ThrowsAsync<FileNotFoundException>(() => fixture.FileStorage.OpenReadAsync(
            orphan.StoragePath,
            orphan.ContentType));
    }

    [Fact]
    public async Task ClamAvScanner_UnavailableEndpointReturnsFailClosedState()
    {
        var scanner = new ClamAvAttachmentMalwareScanner(Options.Create(new AttachmentSecurityOptions
        {
            ScannerProvider = "ClamAv",
            ClamAvHost = "127.0.0.1",
            ClamAvPort = 1,
            ClamAvTimeoutSeconds = 1
        }));

        var result = await scanner.ScanAsync(
            new MemoryStream("scanner unavailable"u8.ToArray()),
            "scan.txt",
            default);

        Assert.Equal(AttachmentMalwareScanStatuses.Unavailable, result.Status);
        Assert.Equal("ClamAV", result.Provider);
    }

    private static WorkItemAttachmentActivityDocument ToDocument(
        StoredAttachment stored,
        DateTimeOffset createdAt) =>
        new()
        {
            OrganizationId = "org",
            ProjectId = "project",
            WorkItemId = "work-item",
            FileName = stored.FileName,
            ContentType = stored.ContentType,
            SizeBytes = stored.SizeBytes,
            StoragePath = stored.StoragePath,
            ChecksumSha256 = stored.ChecksumSha256,
            SecurityState = stored.SecurityState,
            ScanProvider = stored.ScanProvider,
            ScanDetail = stored.ScanDetail,
            ScannedAt = stored.ScannedAt,
            CreatedAt = createdAt
        };

    private static byte[] CreateZip(params (string Name, byte[] Content)[] entries)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var item in entries)
            {
                var entry = archive.CreateEntry(item.Name, CompressionLevel.SmallestSize);
                using var entryStream = entry.Open();
                entryStream.Write(item.Content);
            }
        }

        return output.ToArray();
    }

    private sealed class AttachmentFixture : IAsyncDisposable
    {
        public AttachmentFixture(
            string scannerStatus,
            AttachmentSecurityOptions? options = null,
            FixedClock? clock = null)
        {
            RootPath = Path.Combine(Path.GetTempPath(), "zumbo-attachment-security", Guid.NewGuid().ToString("N"));
            FileStorage = new LocalFileStorage(Microsoft.Extensions.Options.Options.Create(
                new LocalStorageOptions { RootPath = RootPath }));
            Scanner = new MutableScanner(scannerStatus);
            Clock = clock ?? new FixedClock(DateTimeOffset.UtcNow);
            Options = Microsoft.Extensions.Options.Options.Create(options ?? new AttachmentSecurityOptions());
            Adapter = new AttachmentStorageAdapter(FileStorage, Scanner, Options, Clock);
        }

        public string RootPath { get; }
        public LocalFileStorage FileStorage { get; }
        public MutableScanner Scanner { get; }
        public FixedClock Clock { get; }
        public IOptions<AttachmentSecurityOptions> Options { get; }
        public AttachmentStorageAdapter Adapter { get; }

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class MutableScanner(string status) : IAttachmentMalwareScanner
    {
        public string Status { get; set; } = status;

        public Task<AttachmentMalwareScanResult> ScanAsync(Stream content, string fileName, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new AttachmentMalwareScanResult(Status, "TestScanner", "test outcome"));
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class NonSeekableReadStream(byte[] content) : MemoryStream(content)
    {
        public override bool CanSeek => false;
        public override long Position
        {
            get => base.Position;
            set => throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin loc) => throw new NotSupportedException();
    }
}
