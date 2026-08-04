using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.Modules.Boards.Application.Features.BoardsCore;
using Zumbo.Modules.Boards.Application.Features.ColumnOrdering;
using Zumbo.Modules.Boards.Application.Features.Columns;
using Zumbo.Modules.Boards.Application.Features.Lifecycle;
using Zumbo.Modules.Boards.Application.Features.Swimlanes;
using Zumbo.Modules.Boards.Application.Features.Views;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Boards;

public sealed partial class BoardService(
    IDocumentRepository<BoardDocument> boards,
    IBoardProjectAccessChecker accessChecker,
    IBoardColumnUsageChecker usageChecker,
    IDistributedLockProvider distributedLockProvider,
    IOptions<DistributedLockOptions> distributedLockOptions,
    IClock clock,
    ICurrentUser currentUser,
    IBoardAuditWriter audit,
    IExpectedVersionAccessor? expectedVersions = null,
    IBoardWorkflowCatalog? workflowCatalog = null)
{
    private readonly ExpectedVersionState expectedVersion = new(expectedVersions);
    private readonly CreateBoardHandler createBoardHandler = new(
        boards,
        accessChecker,
        distributedLockProvider,
        distributedLockOptions,
        clock,
        currentUser,
        audit);
    private readonly ListBoardsByProjectHandler listBoardsByProjectHandler = new(
        boards,
        accessChecker,
        currentUser);
    private readonly UpdateBoardHandler updateBoardHandler = new(
        boards,
        accessChecker,
        clock,
        currentUser,
        audit,
        expectedVersions);
    private readonly ArchiveBoardHandler archiveBoardHandler = new(
        boards,
        accessChecker,
        usageChecker,
        clock,
        currentUser,
        audit,
        expectedVersions);
    private readonly RestoreBoardHandler restoreBoardHandler = new(
        boards,
        accessChecker,
        clock,
        currentUser,
        audit,
        expectedVersions);
    private readonly UpdateSwimlaneHandler updateSwimlaneHandler = new(
        boards,
        accessChecker,
        clock,
        currentUser,
        audit,
        expectedVersions);
    private readonly AddColumnHandler addColumnHandler = new(
        boards,
        accessChecker,
        clock,
        currentUser,
        audit,
        expectedVersions,
        workflowCatalog);
    private readonly UpdateColumnHandler updateColumnHandler = new(
        boards,
        accessChecker,
        usageChecker,
        clock,
        currentUser,
        audit,
        expectedVersions,
        workflowCatalog);
    private readonly DeleteColumnHandler deleteColumnHandler = new(
        boards,
        accessChecker,
        usageChecker,
        clock,
        currentUser,
        audit,
        expectedVersions);
    private readonly ReorderColumnsHandler reorderColumnsHandler = new(
        boards,
        accessChecker,
        clock,
        currentUser,
        audit,
        expectedVersions);
    private readonly CreateViewHandler createViewHandler = new(
        boards,
        accessChecker,
        clock,
        currentUser,
        audit,
        expectedVersions);
    private readonly UpdateViewHandler updateViewHandler = new(
        boards,
        accessChecker,
        clock,
        currentUser,
        audit,
        expectedVersions);
    private readonly DeleteViewHandler deleteViewHandler = new(
        boards,
        accessChecker,
        clock,
        currentUser,
        audit,
        expectedVersions);
}
