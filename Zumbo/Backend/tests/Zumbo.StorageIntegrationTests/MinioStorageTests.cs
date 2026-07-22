using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Infrastructure.Storage;

namespace Zumbo.StorageIntegrationTests;

public sealed class MinioStorageTests
{
    [Fact]
    public async Task Minio_RoundTripIsPrivateChecksummedAndCleanedUp()
    {
        var endpoint = RequireEnvironment("ZUMBO_TEST_MINIO_ENDPOINT");
        var accessKey = RequireEnvironment("ZUMBO_TEST_MINIO_ACCESS_KEY");
        var secretKey = RequireEnvironment("ZUMBO_TEST_MINIO_SECRET_KEY");
        var bucket = "zumbo-storage-test-" + Guid.NewGuid().ToString("N");
        var options = new MinioStorageOptions
        {
            Endpoint = endpoint,
            AccessKey = accessKey,
            SecretKey = secretKey,
            BucketName = bucket,
            RequestTimeoutSeconds = 5
        };
        var storage = new MinioFileStorage(Options.Create(options));
        StoredFile? stored = null;

        try
        {
            await storage.CheckHealthAsync();
            var bytes = "Zumbo MinIO storage contract"u8.ToArray();
            stored = await storage.SaveQuarantinedAsync(
                new MemoryStream(bytes),
                "minio-contract.txt",
                "text/plain",
                1024);

            Assert.StartsWith("quarantine/", stored.StoragePath, StringComparison.Ordinal);
            var inventory = await storage.ListAttachmentObjectsAsync(1);
            Assert.Single(inventory);
            Assert.Equal(stored.StoragePath, inventory[0].StoragePath);
            stored = await storage.PromoteAsync(stored);
            Assert.StartsWith("attachments/", stored.StoragePath, StringComparison.Ordinal);

            Assert.Equal(bytes.Length, stored.SizeBytes);
            Assert.Equal(
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant(),
                stored.ChecksumSha256);

            var opened = await storage.OpenReadAsync(stored.StoragePath, stored.ContentType);
            await using (opened.Content)
            {
                using var copy = new MemoryStream();
                await opened.Content.CopyToAsync(copy);
                Assert.Equal(bytes, copy.ToArray());
            }

            using var anonymous = new HttpClient();
            var anonymousResponse = await anonymous.GetAsync(
                $"{endpoint.TrimEnd('/')}/{bucket}/{stored.StoragePath}");
            Assert.Equal(HttpStatusCode.Forbidden, anonymousResponse.StatusCode);

            await storage.DeleteAsync(stored.StoragePath);
            await Assert.ThrowsAsync<AmazonS3Exception>(() =>
                storage.OpenReadAsync(stored.StoragePath, stored.ContentType));
        }
        finally
        {
            if (stored is not null)
            {
                await storage.DeleteAsync(stored.StoragePath);
            }

            using var client = CreateClient(options);
            try
            {
                await client.DeleteBucketAsync(bucket);
            }
            catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
            {
            }
        }
    }

    [Fact]
    public async Task Minio_UnavailableEndpointFailsWithinConfiguredTimeout()
    {
        var storage = new MinioFileStorage(Options.Create(new MinioStorageOptions
        {
            Endpoint = "http://127.0.0.1:1",
            AccessKey = "synthetic-test-user",
            SecretKey = "synthetic-test-password",
            BucketName = "zumbo-storage-unavailable-test",
            RequestTimeoutSeconds = 1
        }));

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await Assert.ThrowsAnyAsync<Exception>(() => storage.CheckHealthAsync(timeout.Token));
    }

    private static AmazonS3Client CreateClient(MinioStorageOptions options) => new(
        new BasicAWSCredentials(options.AccessKey, options.SecretKey),
        new AmazonS3Config
        {
            ServiceURL = options.Endpoint,
            ForcePathStyle = true,
            AuthenticationRegion = "us-east-1",
            Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds),
            MaxErrorRetry = 0
        });

    private static string RequireEnvironment(string name) =>
        Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"{name} must be set for the real MinIO integration test.");
}
