using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Runtime;
using System.Security.Cryptography;

namespace Zumbo.BuildingBlocks.Infrastructure.Storage;

public sealed class LocalStorageOptions
{
    public string RootPath { get; init; } = "storage";
    public string PublicBasePath { get; init; } = "/files";
}
