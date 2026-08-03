using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Runtime;
using System.Security.Cryptography;

namespace Zumbo.BuildingBlocks.Infrastructure.Storage;

public static class StorageConfiguration
{
    public static string GetValidatedProvider(IConfiguration configuration)
    {
        var provider = configuration["Storage:Provider"]?.Trim();
        if (string.IsNullOrWhiteSpace(provider))
        {
            throw new InvalidOperationException("Storage:Provider must be configured as Local or Minio.");
        }

        if (provider.Equals("Local", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(configuration["Storage:Local:RootPath"]))
            {
                throw new InvalidOperationException("Storage:Local:RootPath must be configured for the Local provider.");
            }

            return "Local";
        }

        if (!provider.Equals("Minio", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Storage provider '{provider}' is not supported.");
        }

        var endpoint = configuration["Storage:Minio:Endpoint"];
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri)
            || (endpointUri.Scheme != Uri.UriSchemeHttp && endpointUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("Storage:Minio:Endpoint must be an absolute HTTP or HTTPS URL.");
        }

        Require(configuration, "Storage:Minio:AccessKey");
        Require(configuration, "Storage:Minio:SecretKey");
        var bucketName = Require(configuration, "Storage:Minio:BucketName");
        if (bucketName.Length is < 3 or > 63
            || bucketName.StartsWith('-')
            || bucketName.EndsWith('-')
            || bucketName.Any(character => !(char.IsLower(character) || char.IsDigit(character) || character is '-' or '.')))
        {
            throw new InvalidOperationException("Storage:Minio:BucketName must use a valid lowercase S3 bucket name.");
        }

        var timeout = configuration.GetValue<int?>("Storage:Minio:RequestTimeoutSeconds") ?? 10;
        if (timeout is < 1 or > 120)
        {
            throw new InvalidOperationException("Storage:Minio:RequestTimeoutSeconds must be between 1 and 120.");
        }

        return "Minio";
    }

    private static string Require(IConfiguration configuration, string key)
    {
        var value = configuration[key]?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{key} must be configured for the Minio provider.");
        }

        return value;
    }
}
