using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record UpdateWorkItemTemplateRequest(
    string BoardId,
    string Name,
    string Title,
    string? Description,
    string Type,
    string? Priority,
    string? AssigneeUserId,
    string? TeamId,
    int? DueAfterDays,
    IReadOnlyCollection<string>? Labels,
    IReadOnlyCollection<WorkItemCustomFieldValueRequest>? CustomFields);
