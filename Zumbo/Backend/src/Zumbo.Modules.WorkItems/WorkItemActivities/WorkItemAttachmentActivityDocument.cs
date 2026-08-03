using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class WorkItemAttachmentActivityDocument : IWorkItemActivityDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OrganizationId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string WorkItemId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string StoragePath { get; set; } = string.Empty;
    public string ChecksumSha256 { get; set; } = string.Empty;
    public string SecurityState { get; set; } = AttachmentSecurityStates.Clean;
    public string ScanProvider { get; set; } = "Legacy";
    public string? ScanDetail { get; set; }
    public DateTimeOffset? ScannedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long Version { get; set; }
}
