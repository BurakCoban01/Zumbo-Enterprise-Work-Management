using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record StoredAttachment(
    string FileName,
    string ContentType,
    long SizeBytes,
    string StoragePath,
    string ChecksumSha256,
    string SecurityState = AttachmentSecurityStates.Clean,
    string ScanProvider = "Policy",
    string? ScanDetail = null,
    DateTimeOffset? ScannedAt = null);
