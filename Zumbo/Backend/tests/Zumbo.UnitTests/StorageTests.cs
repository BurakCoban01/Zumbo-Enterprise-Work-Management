using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Infrastructure.Storage;

namespace Zumbo.UnitTests;

public sealed class StorageTests
{
    [Fact]
    public async Task LocalStorage_RoundTripUsesSafeKeyAndRecordsChecksum()
    {
        await using var fixture = new LocalStorageFixture();
        var bytes = "Zumbo local storage contract"u8.ToArray();

        var stored = await fixture.Storage.SaveAsync(
            new MemoryStream(bytes),
            "../unsafe-notes.txt",
            "text/plain",
            1024);

        Assert.Equal("unsafe-notes.txt", stored.FileName);
        Assert.DoesNotContain("..", stored.StoragePath, StringComparison.Ordinal);
        Assert.Equal(bytes.Length, stored.SizeBytes);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), stored.ChecksumSha256);

        var opened = await fixture.Storage.OpenReadAsync(stored.StoragePath, stored.ContentType);
        await using (opened.Content)
        {
            using var copy = new MemoryStream();
            await opened.Content.CopyToAsync(copy);
            Assert.Equal(bytes, copy.ToArray());
        }

        await fixture.Storage.DeleteAsync(stored.StoragePath);
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            fixture.Storage.OpenReadAsync(stored.StoragePath, stored.ContentType));
    }

    [Fact]
    public async Task LocalStorage_QuarantinePromotionAndBoundedInventoryArePrivateKeys()
    {
        await using var fixture = new LocalStorageFixture();
        var quarantined = await fixture.Storage.SaveQuarantinedAsync(
            new MemoryStream("quarantine contract"u8.ToArray()),
            "../review.txt",
            "text/plain",
            1024);

        Assert.StartsWith("quarantine/", quarantined.StoragePath, StringComparison.Ordinal);
        Assert.DoesNotContain("..", quarantined.StoragePath, StringComparison.Ordinal);
        var beforePromotion = await fixture.Storage.ListAttachmentObjectsAsync(1);
        Assert.Single(beforePromotion);
        Assert.Equal(quarantined.StoragePath, beforePromotion[0].StoragePath);

        var promoted = await fixture.Storage.PromoteAsync(quarantined);
        Assert.StartsWith("attachments/", promoted.StoragePath, StringComparison.Ordinal);
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            fixture.Storage.OpenReadAsync(quarantined.StoragePath, quarantined.ContentType));
        var opened = await fixture.Storage.OpenReadAsync(promoted.StoragePath, promoted.ContentType);
        await opened.Content.DisposeAsync();
    }

    [Fact]
    public async Task LocalStorage_RejectsEscapingKeysAndRemovesOversizedPartialFile()
    {
        await using var fixture = new LocalStorageFixture();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Storage.OpenReadAsync("../outside.txt", "text/plain"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Storage.DeleteAsync("../outside.txt"));

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Storage.SaveAsync(
            new MemoryStream(new byte[32]),
            "large.bin",
            "application/octet-stream",
            8));
        Assert.Empty(Directory.EnumerateFiles(fixture.RootPath));
    }

    [Fact]
    public async Task LocalStorage_HonorsCancellationBeforeSideEffects()
    {
        await using var fixture = new LocalStorageFixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Storage.SaveAsync(
            new MemoryStream([1, 2, 3]),
            "cancelled.bin",
            "application/octet-stream",
            32,
            cancellation.Token));
        Assert.False(Directory.Exists(fixture.RootPath));
    }

    [Fact]
    public void StorageConfiguration_RejectsUnknownAndInvalidSelectedProviders()
    {
        Assert.Throws<InvalidOperationException>(() => StorageConfiguration.GetValidatedProvider(Configuration(
            ("Storage:Provider", "Unknown"))));

        Assert.Throws<InvalidOperationException>(() => StorageConfiguration.GetValidatedProvider(Configuration(
            ("Storage:Provider", "Local"),
            ("Storage:Local:RootPath", ""))));

        Assert.Throws<InvalidOperationException>(() => StorageConfiguration.GetValidatedProvider(Configuration(
            ("Storage:Provider", "Minio"),
            ("Storage:Minio:Endpoint", "not-a-url"),
            ("Storage:Minio:AccessKey", "test"),
            ("Storage:Minio:SecretKey", "test-secret"),
            ("Storage:Minio:BucketName", "zumbo-test"))));

        var valid = StorageConfiguration.GetValidatedProvider(Configuration(
            ("Storage:Provider", "minio"),
            ("Storage:Minio:Endpoint", "http://127.0.0.1:59000"),
            ("Storage:Minio:AccessKey", "test"),
            ("Storage:Minio:SecretKey", "test-secret"),
            ("Storage:Minio:BucketName", "zumbo-test"),
            ("Storage:Minio:RequestTimeoutSeconds", "2")));

        Assert.Equal("Minio", valid);
    }

    private static IConfiguration Configuration(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(x => x.Key, x => x.Value))
            .Build();

    private sealed class LocalStorageFixture : IAsyncDisposable
    {
        public LocalStorageFixture()
        {
            RootPath = Path.Combine(Path.GetTempPath(), "zumbo-storage-tests", Guid.NewGuid().ToString("N"));
            Storage = new LocalFileStorage(Options.Create(new LocalStorageOptions { RootPath = RootPath }));
        }

        public string RootPath { get; }
        public LocalFileStorage Storage { get; }

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }
}
