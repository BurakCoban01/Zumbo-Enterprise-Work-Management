using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed class WorkItemTemplateDocument : IVersionedDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OrganizationId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string BoardId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Type { get; set; } = "Task";
    public int IssueTypeSchemaVersion { get; set; } = 1;
    public List<WorkItemCustomFieldValueDocument> CustomFields { get; set; } = [];
    public string Priority { get; set; } = "Medium";
    public string? AssigneeUserId { get; set; }
    public string? TeamId { get; set; }
    public int? DueAfterDays { get; set; }
    public List<string> Labels { get; set; } = [];
    public string CreatedByUserId { get; set; } = string.Empty;
    public bool Archived { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; }
}
