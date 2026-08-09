using Zumbo.SharedKernel;

namespace Zumbo.Modules.Projects.Application.Features.Portfolio;

internal sealed class AddInitiativeStatusUpdateSlice(
    PortfolioReadAccess access,
    PortfolioMutationPersistence persistence,
    IPortfolioAuditWriter audit,
    IClock clock)
{
    internal async Task<PortfolioResponse> HandleAsync(
        AddInitiativeStatusUpdateCommand command,
        CancellationToken ct)
    {
        var actor = access.CurrentActor();
        var portfolio = await access.GetDocumentAsync(
            command.PortfolioId,
            includeArchived: false,
            ct);
        PortfolioReadAccess.EnsureVisible(portfolio, actor.UserId);
        var initiative = portfolio.Initiatives.SingleOrDefault(
            item => item.Id == command.InitiativeId)
            ?? throw new NotFoundException("INITIATIVE_NOT_FOUND", "Initiative was not found.");
        if (portfolio.OwnerUserId != actor.UserId && initiative.OwnerUserId != actor.UserId)
        {
            throw new ForbiddenException(
                "Only the portfolio or initiative owner can publish a status update.");
        }
        var status = PortfolioValidation.Allowed(
            command.Request.Status,
            InitiativeStatuses.Allowed,
            "Initiative status");
        var health = PortfolioValidation.Allowed(
            command.Request.Health,
            InitiativeHealth.Allowed,
            "Initiative health");
        var confidence = PortfolioValidation.Confidence(command.Request.Confidence);
        var note = PortfolioValidation.Required(
            command.Request.Note,
            "Status update note",
            1000);
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
        await persistence.ReplaceAsync(portfolio, ct);
        await audit.WriteAsync(
            "InitiativeStatusUpdated",
            portfolio.Id,
            null,
            initiative.Id,
            command.CorrelationId,
            ct);
        return PortfolioResponseMapper.ToResponse(portfolio, actor.UserId);
    }
}
