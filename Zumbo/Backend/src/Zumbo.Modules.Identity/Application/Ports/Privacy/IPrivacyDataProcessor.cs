using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Identity;

public interface IPrivacyDataProcessor
{
    Task<IReadOnlyCollection<PrivacyDataGroup>> ExportAsync(
        string userId,
        string organizationId,
        CancellationToken ct);
    Task<long> WriteExportAsync(
        string userId,
        string organizationId,
        UserProfileResponse profile,
        Stream destination,
        CancellationToken ct);
    Task EnsureCanAnonymizeAsync(string userId, string organizationId, CancellationToken ct);
    Task AnonymizeReferencesAsync(
        string userId,
        string organizationId,
        string pseudonym,
        string username,
        string email,
        CancellationToken ct);
}
