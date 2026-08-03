using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Identity;

public sealed class PrivacyWorkflowDocument : IVersionedDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OrganizationId { get; set; } = string.Empty;
    public string RequestedByUserId { get; set; } = string.Empty;
    public string State { get; set; } = PrivacyWorkflowStates.Pending;
    public string Pseudonym { get; set; } = string.Empty;
    public string StatusTokenHash { get; set; } = string.Empty;
    public int CompletedSteps { get; set; }
    public int TotalSteps { get; set; } = 2;
    public int DispatchSequence { get; set; }
    public int Attempts { get; set; }
    public string? LastErrorCode { get; set; }
    public string? LastErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public long Version { get; set; }
}
