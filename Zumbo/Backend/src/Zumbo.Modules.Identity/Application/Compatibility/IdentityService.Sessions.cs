using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Identity;

public sealed partial class IdentityService
{
    public Task<IReadOnlyList<SessionResponse>> ListSessionsAsync(CancellationToken ct) =>
            ListSessionsAsync(null, ct);

        public async Task<IReadOnlyList<SessionResponse>> ListSessionsAsync(string? currentSessionId, CancellationToken ct) =>
            await listSessionsHandler.HandleAsync(currentSessionId, ct);

    public async Task RevokeSessionAsync(string sessionId, string correlationId, CancellationToken ct)
            => await revokeSessionHandler.HandleAsync(sessionId, correlationId, ct);

        private async Task<int> RevokeSessionAsync(
            RefreshSessionDocument? session,
            DateTimeOffset now,
            CancellationToken ct)
        {
            if (session is null || !session.IsActive(now))
            {
                return 0;
            }

            return await sessions.RevokeAsync(session, now, null, ct) ? 1 : 0;
        }
}
