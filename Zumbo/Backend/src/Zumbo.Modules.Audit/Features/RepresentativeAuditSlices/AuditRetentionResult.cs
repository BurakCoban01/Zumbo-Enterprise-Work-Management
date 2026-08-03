using Zumbo.SharedKernel;

namespace Zumbo.Modules.Audit;
public sealed record AuditRetentionResult(string OrganizationId, DateTimeOffset Cutoff, int Deleted);
