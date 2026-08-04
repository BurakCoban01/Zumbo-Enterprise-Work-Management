using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects;

public sealed partial class PortfolioService{

    public async Task<PortfolioResponse> AddStatusUpdateAsync(
        string portfolioId,
        string initiativeId,
        AddInitiativeStatusUpdateRequest request,
        string correlationId,
        CancellationToken ct)
    {
        var actor = CurrentActor();
        var portfolio = await GetDocumentAsync(portfolioId, includeArchived: false, ct);
        EnsureVisible(portfolio, actor.UserId);
        var initiative = portfolio.Initiatives.SingleOrDefault(item => item.Id == initiativeId)
            ?? throw new NotFoundException("INITIATIVE_NOT_FOUND", "Initiative was not found.");
        if (portfolio.OwnerUserId != actor.UserId && initiative.OwnerUserId != actor.UserId)
            throw new ForbiddenException("Only the portfolio or initiative owner can publish a status update.");
        var status = Allowed(request.Status, InitiativeStatuses.Allowed, "Initiative status");
        var health = Allowed(request.Health, InitiativeHealth.Allowed, "Initiative health");
        var confidence = Confidence(request.Confidence);
        var note = Required(request.Note, "Status update note", 1000);
        initiative.Status = status;
        initiative.Health = health;
        initiative.Confidence = confidence;
        initiative.StatusUpdates.Insert(0, new InitiativeStatusUpdateDocument
        {
            Status = status,
            Health = health,
            Confidence = confidence,
            Note = note,
            AuthorUserId = actor.UserId,
            CreatedAt = clock.UtcNow
        });
        ProjectHistoryRetentionPolicy.RetainMostRecent(
            initiative.StatusUpdates,
            ProjectHistoryRetentionPolicy.MaximumInitiativeStatusUpdates);
        portfolio.UpdatedAt = clock.UtcNow;
        await ReplaceAsync(portfolio, ct);
        await audit.WriteAsync(
            "InitiativeStatusUpdated", portfolio.Id, null, initiative.Id, correlationId, ct);
        return ToResponse(portfolio, actor.UserId);
    }
}
