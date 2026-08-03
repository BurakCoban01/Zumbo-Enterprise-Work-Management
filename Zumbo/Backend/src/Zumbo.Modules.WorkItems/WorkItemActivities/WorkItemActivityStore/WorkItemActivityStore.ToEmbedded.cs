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

public sealed partial class WorkItemActivityStore{

    private static AttachmentDocument ToEmbedded(WorkItemAttachmentActivityDocument source) => new()
    {
        Id = source.Id,
        FileName = source.FileName,
        ContentType = source.ContentType,
        SizeBytes = source.SizeBytes,
            StoragePath = source.StoragePath,
            ChecksumSha256 = source.ChecksumSha256,
            SecurityState = source.SecurityState,
            ScanProvider = source.ScanProvider,
            ScanDetail = source.ScanDetail,
            ScannedAt = source.ScannedAt,
            CreatedAt = source.CreatedAt
    };

    private static WorkLogDocument ToEmbedded(WorkItemWorkLogActivityDocument source) => new()
    {
        Id = source.Id,
        UserId = source.UserId,
        Hours = source.Hours,
        Note = source.Note,
        CreatedAt = source.CreatedAt
    };

    private static WorkItemApprovalDocument ToEmbedded(WorkItemApprovalActivityDocument source) => new()
    {
        Id = source.Id,
        FromStatus = source.FromStatus,
        ToStatus = source.ToStatus,
        RequestedByUserId = source.RequestedByUserId,
        RequestedAt = source.RequestedAt,
        ExpiresAt = source.ExpiresAt,
        Status = source.Status,
        DecidedByUserId = source.DecidedByUserId,
        DecidedAt = source.DecidedAt,
        Note = source.Note,
        ConsumedAt = source.ConsumedAt
    };

    private static WorkItemStatusHistoryDocument ToEmbedded(WorkItemTimelineActivityDocument source) => new()
    {
        FromStatus = source.FromStatus,
        ToStatus = source.ToStatus,
        ChangedByUserId = source.ChangedByUserId,
        ChangedAt = source.ChangedAt
    };
}
