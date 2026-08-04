using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;
public sealed record AttachmentResponse(
    string Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    DateTimeOffset CreatedAt,
    string SecurityState = AttachmentSecurityStates.Clean,
    string ScanProvider = "Legacy",
    DateTimeOffset? ScannedAt = null);
