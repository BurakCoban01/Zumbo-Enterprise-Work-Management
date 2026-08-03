using System.Security.Claims;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Workflows;
using Zumbo.Modules.WorkItems;

public sealed class AutomationActorContextRunner(
    IUserRepository users,
    IHttpContextAccessor httpContextAccessor)
{
    public async Task<T> RunAsync<T>(
        string actorUserId,
        string organizationId,
        string correlationId,
        Func<bool, Task<T>> operation,
        CancellationToken ct)
    {
        var actor = await users.GetByIdAsync(actorUserId, ct);
        var actorAvailable = actor is { IsActive: true }
            && (actor.OrganizationId == organizationId
                || Zumbo.BuildingBlocks.Application.Security.PermissionCatalog
                    .IsSystemAdministrator(actor.Roles));
        var previous = httpContextAccessor.HttpContext;
        try
        {
            if (actorAvailable)
            {
                var context = new DefaultHttpContext { TraceIdentifier = correlationId };
                context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new(ClaimTypes.NameIdentifier, actor!.Id),
                    new("organizationId", actor.OrganizationId),
                    .. actor.Roles.Select(role => new Claim(ClaimTypes.Role, role))
                ], "Automation"));
                httpContextAccessor.HttpContext = context;
            }

            return await operation(actorAvailable);
        }
        finally
        {
            httpContextAccessor.HttpContext = previous;
        }
    }
}
