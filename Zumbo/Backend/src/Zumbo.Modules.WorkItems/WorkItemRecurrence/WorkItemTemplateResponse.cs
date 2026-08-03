using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record WorkItemTemplateResponse(
    string Id,
    string ProjectId,
    string BoardId,
    string Name,
    string Title,
    string Description,
    string Type,
    string Priority,
    string? AssigneeUserId,
    string? TeamId,
    int? DueAfterDays,
    IReadOnlyCollection<string> Labels,
    int IssueTypeSchemaVersion,
    IReadOnlyCollection<WorkItemCustomFieldValueResponse> CustomFields,
    bool Archived,
    long Version) : IVersionedResource;
