using System.Security.Cryptography;
using System.Text;
using System.Linq.Expressions;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class WorkItemCollaborationDocument : IVersionedDocument
{
    public string Id { get; set; } = string.Empty;
    public string OrganizationId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string WorkItemId { get; set; } = string.Empty;
    public List<string> WatcherUserIds { get; set; } = [];
    public List<string> VoterUserIds { get; set; } = [];
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; }
}
