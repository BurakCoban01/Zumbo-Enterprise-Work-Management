using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class WorkItemRecurrenceOccurrenceDocument : IVersionedDocument
{
    public string Id { get; set; } = string.Empty;
    public string OrganizationId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string RecurrenceId { get; set; } = string.Empty;
    public string TemplateId { get; set; } = string.Empty;
    public DateTimeOffset ScheduledForUtc { get; set; }
    public string Status { get; set; } = WorkItemRecurrenceOccurrenceStates.Scheduled;
    public string? CreatedWorkItemId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? GeneratedAt { get; set; }
    public long Version { get; set; }
}
