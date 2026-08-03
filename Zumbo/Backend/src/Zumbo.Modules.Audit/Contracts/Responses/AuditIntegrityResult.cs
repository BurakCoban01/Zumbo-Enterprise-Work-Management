using Zumbo.SharedKernel;

namespace Zumbo.Modules.Audit;
public sealed record AuditIntegrityResult(
    string OrganizationId,
    int Verified,
    bool Valid,
    string? BrokenRecordId,
    bool CompleteHistory = true,
    long FirstSequence = 0,
    string? AnchorHash = null);
