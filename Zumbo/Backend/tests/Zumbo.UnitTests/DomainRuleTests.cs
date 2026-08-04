using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text.Json;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Search;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.BuildingBlocks.Infrastructure.Concurrency;
using Zumbo.BuildingBlocks.Infrastructure.Messaging;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.BuildingBlocks.Infrastructure.Search;
using Zumbo.BuildingBlocks.Infrastructure.Security;
using Zumbo.Modules.Audit;
using Zumbo.Modules.Boards;
using Zumbo.Modules.Boards.Application.Features.BoardsCore;
using Zumbo.Modules.Boards.Application.Features.ColumnOrdering;
using Zumbo.Modules.Boards.Application.Features.Columns;
using Zumbo.Modules.Boards.Application.Features.Lifecycle;
using Zumbo.Modules.Boards.Application.Features.Swimlanes;
using Zumbo.Modules.Boards.Application.Features.Views;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Notifications;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.Projects;
using Zumbo.Modules.Teams;
using Zumbo.Modules.WorkItems;
using Zumbo.Modules.Workflows;
using Zumbo.SharedKernel;

namespace Zumbo.UnitTests;

public sealed class DomainRuleTests
{
    private readonly FixedClock _clock = new();
    private readonly FixedCurrentUser _currentUser = new();

    [Fact]
    public void PasswordHasher_NeverStoresPlainText()
    {
        var hasher = new Pbkdf2PasswordHasher();
        var hash = hasher.Hash("P@ssword123");

        Assert.NotEqual("P@ssword123", hash);
        Assert.True(hasher.Verify("P@ssword123", hash));
        Assert.False(hasher.Verify("wrong", hash));
        Assert.False(hasher.Verify("P@ssword123", "corrupted-password-hash"));
        Assert.False(hasher.Verify("P@ssword123", "PBKDF2-SHA256$2147483647$AA==$AA=="));
    }

    [Fact]
    public async Task InMemoryDistributedLock_AllowsOnlyOneOwnerPerResource()
    {
        var provider = new InMemoryDistributedLockProvider();
        var first = await provider.TryAcquireAsync(
            "board-column:1",
            TimeSpan.FromSeconds(1),
            TimeSpan.Zero);
        Assert.NotNull(first);

        var competing = await provider.TryAcquireAsync(
            "board-column:1",
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(20));
        Assert.Null(competing);

        await first!.DisposeAsync();
        var next = await provider.TryAcquireAsync(
            "board-column:1",
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(20));
        Assert.NotNull(next);
        await next!.DisposeAsync();
    }

    [Fact]
    public async Task Board_DoneColumnCannotBeDeletedDirectly()
    {
        var service = new BoardService(
            new InMemoryDocumentRepository<BoardDocument>(),
            new AllowBoardProjectAccessChecker(),
            new EmptyBoardColumnUsageChecker(),
            new InMemoryDistributedLockProvider(),
            Options.Create(new DistributedLockOptions()),
            _clock,
            _currentUser,
            new RecordingLifecycleAuditWriter());
        var board = await service.CreateAsync(new CreateBoardRequest("project-1", "Delivery", "Kanban"), CancellationToken.None);
        var done = board.Columns.Single(x => x.Category == "Done");

        var error = await Assert.ThrowsAsync<ConflictException>(() =>
            service.DeleteColumnAsync(board.Id, done.Id, CancellationToken.None));

        Assert.Equal("DONE_COLUMN_LOCKED", error.Code);
    }

    [Fact]
    public async Task BoardUpdateHandler_UpdatesBoardAndWritesAudit()
    {
        var boards = new InMemoryDocumentRepository<BoardDocument>();
        var accessChecker = new AllowBoardProjectAccessChecker();
        var audit = new RecordingLifecycleAuditWriter();
        var service = new BoardService(
            boards,
            accessChecker,
            new EmptyBoardColumnUsageChecker(),
            new InMemoryDistributedLockProvider(),
            Options.Create(new DistributedLockOptions()),
            _clock,
            _currentUser,
            audit);
        var board = await service.CreateAsync(
            new CreateBoardRequest("project-1", "Delivery", "Kanban"),
            CancellationToken.None);
        var handler = new UpdateBoardHandler(
            boards,
            accessChecker,
            _clock,
            _currentUser,
            audit);

        var updated = await handler.HandleAsync(
            board.Id,
            new UpdateBoardRequest("Delivery Flow", "Scrum"),
            "board-update-test",
            CancellationToken.None);

        Assert.Equal("Delivery Flow", updated.Name);
        Assert.Equal("Scrum", updated.Type);
        Assert.Contains("BoardUpdated", audit.Actions);
    }

    [Fact]
    public async Task ArchiveBoardHandler_ArchivesUnusedBoardAndWritesAudit()
    {
        var boards = new InMemoryDocumentRepository<BoardDocument>();
        var accessChecker = new AllowBoardProjectAccessChecker();
        var audit = new RecordingLifecycleAuditWriter();
        var service = new BoardService(
            boards,
            accessChecker,
            new EmptyBoardColumnUsageChecker(),
            new InMemoryDistributedLockProvider(),
            Options.Create(new DistributedLockOptions()),
            _clock,
            _currentUser,
            audit);
        var board = await service.CreateAsync(
            new CreateBoardRequest("project-1", "Archive Candidate", "Kanban"),
            CancellationToken.None);
        var handler = new ArchiveBoardHandler(
            boards,
            accessChecker,
            new EmptyBoardColumnUsageChecker(),
            _clock,
            _currentUser,
            audit);

        await handler.HandleAsync(
            new ArchiveBoardCommand(board.Id, "board-archive-test"),
            CancellationToken.None);

        var archived = await boards.SelectAsync(x => x.Id == board.Id, CancellationToken.None);
        Assert.True(archived!.Archived);
        Assert.Contains("BoardArchived", audit.Actions);
    }

    [Fact]
    public async Task RestoreBoardHandler_RestoresArchivedBoardAndWritesAudit()
    {
        var boards = new InMemoryDocumentRepository<BoardDocument>();
        var accessChecker = new AllowBoardProjectAccessChecker();
        var usageChecker = new EmptyBoardColumnUsageChecker();
        var audit = new RecordingLifecycleAuditWriter();
        var service = new BoardService(
            boards,
            accessChecker,
            usageChecker,
            new InMemoryDistributedLockProvider(),
            Options.Create(new DistributedLockOptions()),
            _clock,
            _currentUser,
            audit);
        var board = await service.CreateAsync(
            new CreateBoardRequest("project-1", "Restore Candidate", "Kanban"),
            CancellationToken.None);
        var archiveHandler = new ArchiveBoardHandler(
            boards,
            accessChecker,
            usageChecker,
            _clock,
            _currentUser,
            audit);
        await archiveHandler.HandleAsync(
            new ArchiveBoardCommand(board.Id, "board-archive-test"),
            CancellationToken.None);
        var restoreHandler = new RestoreBoardHandler(
            boards,
            accessChecker,
            _clock,
            _currentUser,
            audit);

        var restored = await restoreHandler.HandleAsync(
            new RestoreBoardCommand(board.Id, "board-restore-test"),
            CancellationToken.None);

        Assert.False(restored.Archived);
        Assert.Contains("BoardRestored", audit.Actions);
    }

    [Fact]
    public async Task BoardUpdateSwimlaneHandler_NormalizesModeAndWritesAudit()
    {
        var boards = new InMemoryDocumentRepository<BoardDocument>();
        var accessChecker = new AllowBoardProjectAccessChecker();
        var audit = new RecordingLifecycleAuditWriter();
        var service = new BoardService(
            boards,
            accessChecker,
            new EmptyBoardColumnUsageChecker(),
            new InMemoryDistributedLockProvider(),
            Options.Create(new DistributedLockOptions()),
            _clock,
            _currentUser,
            audit);
        var board = await service.CreateAsync(
            new CreateBoardRequest("project-1", "Swimlanes", "Kanban"),
            CancellationToken.None);
        var handler = new UpdateSwimlaneHandler(
            boards,
            accessChecker,
            _clock,
            _currentUser,
            audit);

        var updated = await handler.HandleAsync(
            board.Id,
            new UpdateSwimlaneRequest("team"),
            "board-swimlane-test",
            CancellationToken.None);

        Assert.Equal("Team", updated.SwimlaneMode);
        Assert.Contains("BoardSwimlaneUpdated", audit.Actions);
    }

    [Fact]
    public async Task BoardAddColumnHandler_AddsMappedColumnAndWritesAudit()
    {
        var boards = new InMemoryDocumentRepository<BoardDocument>();
        var accessChecker = new AllowBoardProjectAccessChecker();
        var audit = new RecordingLifecycleAuditWriter();
        var service = new BoardService(
            boards,
            accessChecker,
            new EmptyBoardColumnUsageChecker(),
            new InMemoryDistributedLockProvider(),
            Options.Create(new DistributedLockOptions()),
            _clock,
            _currentUser,
            audit);
        var board = await service.CreateAsync(
            new CreateBoardRequest("project-1", "Columns", "Kanban"),
            CancellationToken.None);
        var handler = new AddColumnHandler(
            boards,
            accessChecker,
            _clock,
            _currentUser,
            audit);

        var updated = await handler.HandleAsync(
            board.Id,
            new CreateColumnRequest("Ready for Release", "Custom", 2, ["Ready for Release"]),
            "board-column-test",
            CancellationToken.None);

        Assert.Contains(updated.Columns, column => column.Name == "Ready for Release" && column.WipLimit == 2);
        Assert.Contains("BoardColumnCreated", audit.Actions);
    }

    [Fact]
    public async Task BoardUpdateColumnHandler_UpdatesCustomColumnAndWritesAudit()
    {
        var boards = new InMemoryDocumentRepository<BoardDocument>();
        var accessChecker = new AllowBoardProjectAccessChecker();
        var usageChecker = new EmptyBoardColumnUsageChecker();
        var audit = new RecordingLifecycleAuditWriter();
        var service = new BoardService(
            boards,
            accessChecker,
            usageChecker,
            new InMemoryDistributedLockProvider(),
            Options.Create(new DistributedLockOptions()),
            _clock,
            _currentUser,
            audit);
        var board = await service.CreateAsync(
            new CreateBoardRequest("project-1", "Column Update", "Kanban"),
            CancellationToken.None);
        var addHandler = new AddColumnHandler(boards, accessChecker, _clock, _currentUser, audit);
        board = await addHandler.HandleAsync(
            board.Id,
            new CreateColumnRequest("Ready", "Custom", 2, ["Ready"]),
            "board-column-add-test",
            CancellationToken.None);
        var column = board.Columns.Single(x => x.Name == "Ready");
        var handler = new UpdateColumnHandler(
            boards,
            accessChecker,
            usageChecker,
            _clock,
            _currentUser,
            audit);

        var updated = await handler.HandleAsync(
            board.Id,
            column.Id,
            new UpdateColumnRequest("Ready to Ship", "Custom", 3, ["Ready to Ship"]),
            "board-column-update-test",
            CancellationToken.None);

        Assert.Contains(updated.Columns, item => item.Id == column.Id && item.Name == "Ready to Ship" && item.WipLimit == 3);
        Assert.Contains("BoardColumnUpdated", audit.Actions);
    }

    [Fact]
    public async Task BoardDeleteColumnHandler_DeletesUnusedCustomColumnAndWritesAudit()
    {
        var boards = new InMemoryDocumentRepository<BoardDocument>();
        var accessChecker = new AllowBoardProjectAccessChecker();
        var usageChecker = new EmptyBoardColumnUsageChecker();
        var audit = new RecordingLifecycleAuditWriter();
        var service = new BoardService(
            boards,
            accessChecker,
            usageChecker,
            new InMemoryDistributedLockProvider(),
            Options.Create(new DistributedLockOptions()),
            _clock,
            _currentUser,
            audit);
        var board = await service.CreateAsync(
            new CreateBoardRequest("project-1", "Column Delete", "Kanban"),
            CancellationToken.None);
        var addHandler = new AddColumnHandler(boards, accessChecker, _clock, _currentUser, audit);
        board = await addHandler.HandleAsync(
            board.Id,
            new CreateColumnRequest("Temporary", "Custom", null, ["Temporary"]),
            "board-column-add-delete-test",
            CancellationToken.None);
        var column = board.Columns.Single(x => x.Name == "Temporary");
        var handler = new DeleteColumnHandler(
            boards,
            accessChecker,
            usageChecker,
            _clock,
            _currentUser,
            audit);

        var updated = await handler.HandleAsync(
            new DeleteColumnCommand(board.Id, column.Id, "board-column-delete-test"),
            CancellationToken.None);

        Assert.DoesNotContain(updated.Columns, item => item.Id == column.Id);
        Assert.Equal(
            Enumerable.Range(1, updated.Columns.Count),
            updated.Columns.OrderBy(item => item.Position).Select(item => item.Position));
        Assert.Contains("BoardColumnDeleted", audit.Actions);
    }

    [Fact]
    public async Task BoardReorderColumnsHandler_ReordersEveryColumnAndWritesAudit()
    {
        var boards = new InMemoryDocumentRepository<BoardDocument>();
        var accessChecker = new AllowBoardProjectAccessChecker();
        var audit = new RecordingLifecycleAuditWriter();
        var service = new BoardService(
            boards,
            accessChecker,
            new EmptyBoardColumnUsageChecker(),
            new InMemoryDistributedLockProvider(),
            Options.Create(new DistributedLockOptions()),
            _clock,
            _currentUser,
            audit);
        var board = await service.CreateAsync(
            new CreateBoardRequest("project-1", "Column Order", "Kanban"),
            CancellationToken.None);
        var expectedOrder = board.Columns
            .OrderByDescending(column => column.Position)
            .Select(column => column.Id)
            .ToArray();
        var handler = new ReorderColumnsHandler(
            boards,
            accessChecker,
            _clock,
            _currentUser,
            audit);

        var updated = await handler.HandleAsync(
            board.Id,
            new ReorderColumnsRequest(expectedOrder),
            "board-column-reorder-test",
            CancellationToken.None);

        Assert.Equal(expectedOrder, updated.Columns.OrderBy(column => column.Position).Select(column => column.Id));
        Assert.Contains("BoardColumnsReordered", audit.Actions);
    }

    [Fact]
    public async Task BoardCreateViewHandler_CreatesNormalizedPersonalViewAndWritesAudit()
    {
        var boards = new InMemoryDocumentRepository<BoardDocument>();
        var accessChecker = new AllowBoardProjectAccessChecker();
        var audit = new RecordingLifecycleAuditWriter();
        var service = new BoardService(
            boards,
            accessChecker,
            new EmptyBoardColumnUsageChecker(),
            new InMemoryDistributedLockProvider(),
            Options.Create(new DistributedLockOptions()),
            _clock,
            _currentUser,
            audit);
        var board = await service.CreateAsync(
            new CreateBoardRequest("project-1", "Saved Views", "Kanban"),
            CancellationToken.None);
        var handler = new CreateViewHandler(
            boards,
            accessChecker,
            _clock,
            _currentUser,
            audit);

        var updated = await handler.HandleAsync(
            board.Id,
            new CreateBoardViewRequest(
                "  My urgent work  ",
                false,
                "priority",
                new BoardFilterRequest(" user-1 ", null, [" In Progress "], ["High"], ["urgent"], " api ")),
            "board-view-create-test",
            CancellationToken.None);

        var view = Assert.Single(updated.Views);
        Assert.Equal("My urgent work", view.Name);
        Assert.Equal("Priority", view.SwimlaneMode);
        Assert.Equal("user-1", view.Filter.AssigneeUserId);
        Assert.Equal(["In Progress"], view.Filter.Statuses);
        Assert.Equal("api", view.Filter.Text);
        Assert.Contains("BoardViewCreated", audit.Actions);
    }

    [Fact]
    public async Task BoardUpdateViewHandler_UpdatesOwnedPersonalViewAndWritesAudit()
    {
        var boards = new InMemoryDocumentRepository<BoardDocument>();
        var accessChecker = new AllowBoardProjectAccessChecker();
        var audit = new RecordingLifecycleAuditWriter();
        var service = new BoardService(
            boards,
            accessChecker,
            new EmptyBoardColumnUsageChecker(),
            new InMemoryDistributedLockProvider(),
            Options.Create(new DistributedLockOptions()),
            _clock,
            _currentUser,
            audit);
        var board = await service.CreateAsync(
            new CreateBoardRequest("project-1", "Saved View Update", "Kanban"),
            CancellationToken.None);
        var createHandler = new CreateViewHandler(boards, accessChecker, _clock, _currentUser, audit);
        board = await createHandler.HandleAsync(
            board.Id,
            new CreateBoardViewRequest(
                "My work",
                false,
                "none",
                new BoardFilterRequest(null, null, [], [], [], null)),
            "board-view-add-update-test",
            CancellationToken.None);
        var view = Assert.Single(board.Views);
        var handler = new UpdateViewHandler(boards, accessChecker, _clock, _currentUser, audit);

        var updated = await handler.HandleAsync(
            board.Id,
            view.Id,
            new UpdateBoardViewRequest(
                "My priority work",
                false,
                "priority",
                new BoardFilterRequest(null, "team-1", ["In Progress"], ["High"], [], " release ")),
            "board-view-update-test",
            CancellationToken.None);

        var updatedView = Assert.Single(updated.Views);
        Assert.Equal("My priority work", updatedView.Name);
        Assert.Equal("Priority", updatedView.SwimlaneMode);
        Assert.Equal("team-1", updatedView.Filter.TeamId);
        Assert.Equal("release", updatedView.Filter.Text);
        Assert.Contains("BoardViewUpdated", audit.Actions);
    }

    [Fact]
    public async Task BoardDeleteViewHandler_DeletesOwnedPersonalViewAndWritesAudit()
    {
        var boards = new InMemoryDocumentRepository<BoardDocument>();
        var accessChecker = new AllowBoardProjectAccessChecker();
        var audit = new RecordingLifecycleAuditWriter();
        var service = new BoardService(
            boards,
            accessChecker,
            new EmptyBoardColumnUsageChecker(),
            new InMemoryDistributedLockProvider(),
            Options.Create(new DistributedLockOptions()),
            _clock,
            _currentUser,
            audit);
        var board = await service.CreateAsync(
            new CreateBoardRequest("project-1", "Saved View Delete", "Kanban"),
            CancellationToken.None);
        var createHandler = new CreateViewHandler(boards, accessChecker, _clock, _currentUser, audit);
        board = await createHandler.HandleAsync(
            board.Id,
            new CreateBoardViewRequest(
                "Temporary view",
                false,
                "none",
                new BoardFilterRequest(null, null, [], [], [], null)),
            "board-view-add-delete-test",
            CancellationToken.None);
        var view = Assert.Single(board.Views);
        var handler = new DeleteViewHandler(boards, accessChecker, _clock, _currentUser, audit);

        var updated = await handler.HandleAsync(
            board.Id,
            view.Id,
            "board-view-delete-test",
            CancellationToken.None);

        Assert.Empty(updated.Views);
        Assert.Contains("BoardViewDeleted", audit.Actions);
    }

    [Fact]
    public async Task Board_SwimlaneAndSavedViewsEnforcePersonalVisibility()
    {
        var audit = new RecordingLifecycleAuditWriter();
        var service = new BoardService(
            new InMemoryDocumentRepository<BoardDocument>(),
            new AllowBoardProjectAccessChecker(),
            new EmptyBoardColumnUsageChecker(),
            new InMemoryDistributedLockProvider(),
            Options.Create(new DistributedLockOptions()),
            _clock,
            _currentUser,
            audit);
        var board = await service.CreateAsync(
            new CreateBoardRequest("project-1", "Filtered Board", "Kanban"),
            CancellationToken.None);
        board = await service.UpdateSwimlaneAsync(
            board.Id,
            new UpdateSwimlaneRequest("Team"),
            CancellationToken.None);
        board = await service.CreateViewAsync(
            board.Id,
            new CreateBoardViewRequest(
                "My urgent work",
                false,
                "Priority",
                new BoardFilterRequest("user-1", null, ["In Progress"], ["High"], ["urgent"], "api")),
            CancellationToken.None);
        var privateView = board.Views.Single();
        board = await service.CreateViewAsync(
            board.Id,
            new CreateBoardViewRequest(
                "Team queue",
                true,
                "Team",
                new BoardFilterRequest(null, "team-1", [], [], [], null)),
            CancellationToken.None);

        Assert.Equal("Team", board.SwimlaneMode);
        Assert.Equal(2, board.Views.Count);
        _currentUser.UserId = "user-2";
        board = (await service.ListByProjectAsync("project-1", CancellationToken.None)).Single();
        Assert.Single(board.Views);
        Assert.True(board.Views.Single().IsShared);
        await Assert.ThrowsAsync<NotFoundException>(() => service.UpdateViewAsync(
            board.Id,
            privateView.Id,
            new UpdateBoardViewRequest(
                "Stolen",
                false,
                "None",
            new BoardFilterRequest(null, null, [], [], [], null)),
            CancellationToken.None));
        Assert.Contains("BoardCreated", audit.Actions);
        Assert.Contains("BoardSwimlaneUpdated", audit.Actions);
        Assert.Equal(2, audit.Actions.Count(x => x == "BoardViewCreated"));
    }

    [Fact]
    public async Task WorkItem_CannotJumpFromTodoToDone()
    {
        var service = CreateWorkItemService();
        var item = await service.CreateAsync(
            new CreateWorkItemRequest("project-1", "board-1", "Build API", "Task", "High", "user-2", null),
            "test-correlation",
            CancellationToken.None);

        var error = await Assert.ThrowsAsync<ConflictException>(() =>
            service.MoveAsync(item.Id, new MoveWorkItemRequest("Done"), "test-correlation", CancellationToken.None));

        Assert.Equal("WORKFLOW_TRANSITION_FORBIDDEN", error.Code);
    }

    [Fact]
    public async Task WorkItem_CreateAndMovePublishBoundedRealtimeEvents()
    {
        var realtime = new RecordingWorkItemRealtimePublisher();
        var service = CreateWorkItemService(realtimePublisher: realtime);
        var item = await service.CreateAsync(
            new CreateWorkItemRequest("project-1", "board-1", "Realtime task", "Task", "High", "user-2", null),
            "create-correlation",
            CancellationToken.None);

        item = await service.MoveAsync(
            item.Id,
            new MoveWorkItemRequest("In Progress"),
            "move-correlation",
            CancellationToken.None);

        Assert.Collection(
            realtime.Changes,
            created =>
            {
                Assert.Equal("created", created.EventType);
                Assert.Equal("project-1", created.ProjectId);
                Assert.Equal("create-correlation", created.CorrelationId);
                Assert.Equal(WorkItemRealtimeProtocol.CurrentSchemaVersion, created.SchemaVersion);
                Assert.Equal(created.WorkItem.Version, created.ResourceVersion);
                Assert.True(created.ResourceVersion > 0);
            },
            moved =>
            {
                Assert.Equal("moved", moved.EventType);
                Assert.Equal("In Progress", moved.WorkItem.Status);
                Assert.Equal("move-correlation", moved.CorrelationId);
                Assert.Equal(moved.WorkItem.Version, moved.ResourceVersion);
                Assert.True(moved.ResourceVersion > createdVersion(realtime.Changes));
                Assert.True(JsonSerializer.SerializeToUtf8Bytes(moved).Length < 2_048);
            });

        static long createdVersion(IReadOnlyList<WorkItemRealtimeChange> changes) =>
            changes[0].ResourceVersion;
    }

    [Fact]
    public async Task WorkItem_ReorderPersistsRankAndBoardQueryOrder()
    {
        var service = CreateWorkItemService();
        var first = await service.CreateAsync(
            new CreateWorkItemRequest("project-1", "board-1", "First", "Task", "Medium", null, null),
            "test-correlation",
            CancellationToken.None);
        var second = await service.CreateAsync(
            new CreateWorkItemRequest("project-1", "board-1", "Second", "Task", "Medium", null, null),
            "test-correlation",
            CancellationToken.None);
        var third = await service.CreateAsync(
            new CreateWorkItemRequest("project-1", "board-1", "Third", "Task", "Medium", null, null),
            "test-correlation",
            CancellationToken.None);

        third = await service.ReorderAsync(
            third.Id,
            new ReorderWorkItemRequest(first.Id, null),
            "test-correlation",
            CancellationToken.None);
        var ordered = await service.SearchAsync(
            new WorkItemSearchRequest("project-1", null, "To Do", null),
            CancellationToken.None);

        Assert.True(first.Rank < second.Rank);
        Assert.True(third.Rank < first.Rank);
        Assert.Equal([third.Id, first.Id, second.Id], ordered.Select(item => item.Id));
    }

    [Fact]
    public async Task WorkItem_ArchiveAndRestorePreserveDetailAndListMembership()
    {
        var realtime = new RecordingWorkItemRealtimePublisher();
        var service = CreateWorkItemService(realtimePublisher: realtime);
        var item = await service.CreateAsync(
            new CreateWorkItemRequest("project-1", "board-1", "Recoverable task", "Task", "Medium", null, null),
            "create-correlation",
            CancellationToken.None);
        item = await service.UpdateAsync(
            item.Id,
            new UpdateWorkItemRequest(item.Title, "Kept through the lifecycle", item.Priority, null),
            "update-correlation",
            CancellationToken.None);

        await service.ArchiveAsync(item.Id, "archive-correlation", CancellationToken.None);

        var activeAfterArchive = await service.SearchAsync(
            new WorkItemSearchRequest("project-1", null, null, null),
            CancellationToken.None);
        var archive = await service.SearchAsync(
            new WorkItemSearchRequest("project-1", null, null, "lifecycle", 1, 20, true),
            CancellationToken.None);

        Assert.Empty(activeAfterArchive);
        var archived = Assert.Single(archive);
        Assert.True(archived.Archived);
        Assert.Equal("Kept through the lifecycle", archived.Description);
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync(item.Id, CancellationToken.None));

        var restored = await service.RestoreAsync(item.Id, "restore-correlation", CancellationToken.None);
        var activeAfterRestore = await service.SearchAsync(
            new WorkItemSearchRequest("project-1", null, null, null),
            CancellationToken.None);

        Assert.False(restored.Archived);
        Assert.Equal(item.Id, Assert.Single(activeAfterRestore).Id);
        Assert.Contains(realtime.Changes, change => change.EventType == "archived" && change.WorkItemId == item.Id);
        Assert.Contains(realtime.Changes, change => change.EventType == "restored" && change.WorkItemId == item.Id);
    }

    [Fact]
    public async Task WorkItem_SearchUsesBoundedTenantFallbackWhenSearchIsUnavailable()
    {
        var repository = new InMemoryDocumentRepository<WorkItemDocument>();
        foreach (var id in new[] { "item-1", "item-2", "item-3" })
        {
            await repository.CreateAsync(new WorkItemDocument
            {
                Id = id,
                ProjectId = "project-1",
                BoardId = "board-1",
                ColumnId = "column-1",
                Title = $"Fallback {id}",
                Description = "bounded search",
                CreatedAt = _clock.UtcNow,
                UpdatedAt = _clock.UtcNow
            });
        }
        var service = CreateWorkItemService(
            repository,
            searchIndex: new UnavailableWorkItemSearchIndex(),
            degradedFallbackMaxItems: 2);

        var databasePage = await service.SearchPageAsync(
            new WorkItemSearchRequest("project-1", null, null, null, 1, 1),
            CancellationToken.None);

        var page = await service.SearchPageAsync(
            new WorkItemSearchRequest("project-1", null, null, "fallback", 1, 100),
            CancellationToken.None);

        Assert.False(databasePage.Degraded);
        Assert.Single(databasePage.Items);
        Assert.Equal(3, databasePage.TotalCount);
        Assert.True(page.Degraded);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(["item-1", "item-2"], page.Items.Select(x => x.Id));
    }

    [Fact]
    public async Task Identity_RegisterAndLogin_UsesHashedPasswordAndTokens()
    {
        var repository = new InMemoryDocumentRepository<UserDocument>();
        var users = new UserRepository(repository);
        var sessionDocuments = new InMemoryDocumentRepository<RefreshSessionDocument>();
        var service = new IdentityService(
            users,
            new RefreshSessionStore(sessionDocuments),
            new InMemoryDurableTransactionRunner(),
            new Pbkdf2PasswordHasher(),
            new JwtTokenIssuer(),
            Options.Create(new JwtOptions { SigningKey = "unit-test-signing-key-with-more-than-32-chars" }),
            Options.Create(new LoginSecurityOptions()),
            Options.Create(new IdentityBootstrapOptions()),
            Options.Create(new PasswordResetOptions()),
            new RecordingPasswordResetNotifier(),
            new PlainMfaSecretProtector(),
            new InMemoryDistributedLockProvider(),
            Options.Create(new DistributedLockOptions()),
            _clock,
            _currentUser);

        var registration = await service.RegisterAsync(
            new RegisterUserRequest("alice", "alice@zumbo.local", "P@ssword123", "org-1"),
            CancellationToken.None);

        var login = await service.LoginAsync(new LoginRequest("alice", "P@ssword123"), CancellationToken.None);
        var stored = await users.GetByUsernameOrEmailAsync("alice", CancellationToken.None);

        Assert.NotNull(stored);
        Assert.NotEqual("P@ssword123", stored!.PasswordHash);
        Assert.False(string.IsNullOrWhiteSpace(login.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(login.RefreshToken));
        var storedSessions = await sessionDocuments.ListByFilterAsync(
            x => x.UserId == stored.Id && x.OrganizationId == stored.OrganizationId,
            cancellationToken: CancellationToken.None);
        Assert.DoesNotContain(storedSessions, x => x.TokenHash == registration.RefreshToken);
        Assert.Contains(storedSessions, x => x.TokenHash == RefreshTokenSecurity.Hash(registration.RefreshToken));
        Assert.All(storedSessions, x => Assert.False(string.IsNullOrWhiteSpace(x.Id)));
        Assert.Empty(stored.RefreshTokens);
    }

    [Fact]
    public async Task Identity_ConcurrentRefreshConsumesOnceAndReuseRevokesReplacement()
    {
        var documents = new InMemoryDocumentRepository<UserDocument>();
        var users = new UserRepository(documents);
        var sessionDocuments = new InMemoryDocumentRepository<RefreshSessionDocument>();
        var coordinatedStore = new CoordinatedRefreshSessionStore(new RefreshSessionStore(sessionDocuments));
        var service = new IdentityService(
            users,
            coordinatedStore,
            new InMemoryDurableTransactionRunner(),
            new Pbkdf2PasswordHasher(),
            new JwtTokenIssuer(),
            Options.Create(new JwtOptions { SigningKey = "unit-test-signing-key-with-more-than-32-chars" }),
            Options.Create(new LoginSecurityOptions()),
            Options.Create(new IdentityBootstrapOptions()),
            Options.Create(new PasswordResetOptions()),
            new RecordingPasswordResetNotifier(),
            new PlainMfaSecretProtector(),
            new InMemoryDistributedLockProvider(),
            Options.Create(new DistributedLockOptions()),
            _clock,
            _currentUser);
        var registration = await service.RegisterAsync(
            new RegisterUserRequest("refresh-race", "refresh-race@zumbo.local", "P@ssword123", "org-race"),
            CancellationToken.None);
        coordinatedStore.CoordinateNextPair();

        var attempts = await Task.WhenAll(
            CaptureRefreshAsync(service, registration.RefreshToken),
            CaptureRefreshAsync(service, registration.RefreshToken));

        var succeeded = Assert.Single(attempts, x => x.Response is not null);
        Assert.Single(attempts, x => x.Error is UnauthorizedException);
        await Assert.ThrowsAsync<UnauthorizedException>(() => service.RefreshAsync(
            new RefreshTokenRequest(succeeded.Response!.RefreshToken),
            CancellationToken.None));
        var sessions = await sessionDocuments.ListByFilterAsync(
            x => x.UserId == registration.User.Id,
            cancellationToken: CancellationToken.None);
        Assert.Equal(2, sessions.Count);
        Assert.All(sessions, session => Assert.NotNull(session.RevokedAt));
    }

    [Fact]
    public async Task Identity_LegacyEmbeddedRefreshTokenImportsLazilyWithoutDeletingFallback()
    {
        var documents = new InMemoryDocumentRepository<UserDocument>();
        var users = new UserRepository(documents);
        var rawToken = "legacy-refresh-token-" + Guid.NewGuid().ToString("N");
        var legacySessionId = Guid.NewGuid().ToString("N");
        var user = new UserDocument
        {
            Username = "legacy-refresh",
            Email = "legacy-refresh@zumbo.local",
            OrganizationId = "legacy-org",
            PasswordHash = new Pbkdf2PasswordHasher().Hash("P@ssword123"),
            CreatedAt = _clock.UtcNow,
            RefreshTokens =
            [
                new RefreshTokenDocument
                {
                    SessionId = legacySessionId,
                    TokenHash = RefreshTokenSecurity.Hash(rawToken),
                    CreatedAt = _clock.UtcNow,
                    ExpiresAt = _clock.UtcNow.AddDays(7)
                }
            ]
        };
        await users.AddAsync(user, CancellationToken.None);
        var sessionDocuments = new InMemoryDocumentRepository<RefreshSessionDocument>();
        var service = new IdentityService(
            users,
            new RefreshSessionStore(sessionDocuments),
            new InMemoryDurableTransactionRunner(),
            new Pbkdf2PasswordHasher(),
            new JwtTokenIssuer(),
            Options.Create(new JwtOptions { SigningKey = "unit-test-signing-key-with-more-than-32-chars" }),
            Options.Create(new LoginSecurityOptions()),
            Options.Create(new IdentityBootstrapOptions()),
            Options.Create(new PasswordResetOptions()),
            new RecordingPasswordResetNotifier(),
            new PlainMfaSecretProtector(),
            new InMemoryDistributedLockProvider(),
            Options.Create(new DistributedLockOptions()),
            _clock,
            _currentUser);

        var refreshed = await service.RefreshAsync(new RefreshTokenRequest(rawToken), CancellationToken.None);
        var imported = await sessionDocuments.SelectAsync(x => x.Id == legacySessionId, CancellationToken.None);
        var storedUser = await users.GetByIdAsync(user.Id, CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(refreshed.RefreshToken));
        Assert.NotNull(imported?.RevokedAt);
        Assert.Single(storedUser!.RefreshTokens);
        Assert.Equal(2, await sessionDocuments.CountByFilterAsync(x => x.UserId == user.Id));
    }

    [Fact]
    public async Task Identity_PasswordChangeRevokesLegacyTokenBeforeMigrationRuns()
    {
        var documents = new InMemoryDocumentRepository<UserDocument>();
        var users = new UserRepository(documents);
        var rawToken = "unmigrated-refresh-" + Guid.NewGuid().ToString("N");
        var hasher = new Pbkdf2PasswordHasher();
        var user = new UserDocument
        {
            Username = "unmigrated-user",
            Email = "unmigrated-user@zumbo.local",
            OrganizationId = "unmigrated-org",
            PasswordHash = hasher.Hash("P@ssword123"),
            CreatedAt = _clock.UtcNow,
            RefreshTokens =
            [
                new RefreshTokenDocument
                {
                    TokenHash = RefreshTokenSecurity.Hash(rawToken),
                    CreatedAt = _clock.UtcNow,
                    ExpiresAt = _clock.UtcNow.AddDays(7)
                }
            ]
        };
        await users.AddAsync(user, CancellationToken.None);
        _currentUser.UserId = user.Id;
        _currentUser.OrganizationId = user.OrganizationId;
        var service = new IdentityService(
            users,
            new RefreshSessionStore(new InMemoryDocumentRepository<RefreshSessionDocument>()),
            new InMemoryDurableTransactionRunner(),
            hasher,
            new JwtTokenIssuer(),
            Options.Create(new JwtOptions { SigningKey = "unit-test-signing-key-with-more-than-32-chars" }),
            Options.Create(new LoginSecurityOptions()),
            Options.Create(new IdentityBootstrapOptions()),
            Options.Create(new PasswordResetOptions()),
            new RecordingPasswordResetNotifier(),
            new PlainMfaSecretProtector(),
            new InMemoryDistributedLockProvider(),
            Options.Create(new DistributedLockOptions()),
            _clock,
            _currentUser);

        _ = await service.ChangePasswordAsync(
            new ChangePasswordRequest("P@ssword123", "N3wP@ssword456"),
            CancellationToken.None);
        var stored = await users.GetByIdAsync(user.Id, CancellationToken.None);

        Assert.NotNull(Assert.Single(stored!.RefreshTokens).RevokedAt);
        await Assert.ThrowsAsync<UnauthorizedException>(() => service.RefreshAsync(
            new RefreshTokenRequest(rawToken),
            CancellationToken.None));
    }

    [Fact]
    public async Task Identity_PasswordResetIsOpaqueSingleUseAndRevokesSessions()
    {
        var documents = new InMemoryDocumentRepository<UserDocument>();
        var users = new UserRepository(documents);
        var sessionDocuments = new InMemoryDocumentRepository<RefreshSessionDocument>();
        var notifier = new RecordingPasswordResetNotifier();
        var service = new IdentityService(
            users,
            new RefreshSessionStore(sessionDocuments),
            new InMemoryDurableTransactionRunner(),
            new Pbkdf2PasswordHasher(),
            new JwtTokenIssuer(),
            Options.Create(new JwtOptions { SigningKey = "unit-test-signing-key-with-more-than-32-chars" }),
            Options.Create(new LoginSecurityOptions()),
            Options.Create(new IdentityBootstrapOptions()),
            Options.Create(new PasswordResetOptions { ExpiryMinutes = 30 }),
            notifier,
            new PlainMfaSecretProtector(),
            new InMemoryDistributedLockProvider(),
            Options.Create(new DistributedLockOptions()),
            _clock,
            _currentUser);
        var registration = await service.RegisterAsync(
            new RegisterUserRequest("reset-user", "reset@zumbo.local", "P@ssword123", "org-1"),
            CancellationToken.None);

        var unknown = await service.ForgotPasswordAsync(
            new ForgotPasswordRequest("unknown@zumbo.local"),
            CancellationToken.None);
        var requested = await service.ForgotPasswordAsync(
            new ForgotPasswordRequest("reset@zumbo.local"),
            CancellationToken.None);
        var token = Assert.Single(notifier.Tokens).Token;
        var stored = await users.GetByUsernameOrEmailAsync("reset-user", CancellationToken.None);
        Assert.True(unknown.Accepted);
        Assert.True(requested.Accepted);
        Assert.NotEqual(token, stored!.PasswordResetTokenHash);

        var reset = await service.ResetPasswordAsync(
            new ResetPasswordRequest(token, "N3wP@ssword456"),
            CancellationToken.None);
        Assert.True(reset.Reset);
        await Assert.ThrowsAsync<UnauthorizedException>(() => service.ResetPasswordAsync(
            new ResetPasswordRequest(token, "An0therP@ss789"),
            CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedException>(() => service.LoginAsync(
            new LoginRequest("reset-user", "P@ssword123"),
            CancellationToken.None));
        var login = await service.LoginAsync(
            new LoginRequest("reset-user", "N3wP@ssword456"),
            CancellationToken.None);
        Assert.False(string.IsNullOrWhiteSpace(login.AccessToken));
        await Assert.ThrowsAsync<UnauthorizedException>(() => service.RefreshAsync(
            new RefreshTokenRequest(registration.RefreshToken),
            CancellationToken.None));

        await service.ForgotPasswordAsync(new ForgotPasswordRequest("reset@zumbo.local"), CancellationToken.None);
        var expiringToken = notifier.Tokens.Last().Token;
        _clock.UtcNow = _clock.UtcNow.AddMinutes(31);
        await Assert.ThrowsAsync<UnauthorizedException>(() => service.ResetPasswordAsync(
            new ResetPasswordRequest(expiringToken, "An0therP@ss789"),
            CancellationToken.None));
    }

    [Fact]
    public async Task Identity_LoginLockoutUsesConfiguredPolicyAndResetsAfterExpiry()
    {
        var repository = new InMemoryDocumentRepository<UserDocument>();
        var users = new UserRepository(repository);
        var sessionDocuments = new InMemoryDocumentRepository<RefreshSessionDocument>();
        var service = new IdentityService(
            users,
            new RefreshSessionStore(sessionDocuments),
            new InMemoryDurableTransactionRunner(),
            new Pbkdf2PasswordHasher(),
            new JwtTokenIssuer(),
            Options.Create(new JwtOptions { SigningKey = "unit-test-signing-key-with-more-than-32-chars" }),
            Options.Create(new LoginSecurityOptions { MaxFailedAttempts = 3, LockoutMinutes = 2 }),
            Options.Create(new IdentityBootstrapOptions()),
            Options.Create(new PasswordResetOptions()),
            new RecordingPasswordResetNotifier(),
            new PlainMfaSecretProtector(),
            new InMemoryDistributedLockProvider(),
            Options.Create(new DistributedLockOptions()),
            _clock,
            _currentUser);
        await service.RegisterAsync(
            new RegisterUserRequest("locked-user", "locked@zumbo.local", "P@ssword123", "org-1"),
            CancellationToken.None);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            await Assert.ThrowsAsync<UnauthorizedException>(() => service.LoginAsync(
                new LoginRequest("locked-user", "Wr0ngP@ssword"),
                CancellationToken.None));
        }

        var locked = await users.GetByUsernameOrEmailAsync("locked-user", CancellationToken.None);
        Assert.Equal(3, locked!.FailedLoginCount);
        Assert.Equal(_clock.UtcNow.AddMinutes(2), locked.LockedUntil);
        await Assert.ThrowsAsync<UnauthorizedException>(() => service.LoginAsync(
            new LoginRequest("locked-user", "P@ssword123"),
            CancellationToken.None));

        _clock.UtcNow = _clock.UtcNow.AddMinutes(3);
        var login = await service.LoginAsync(
            new LoginRequest("locked@zumbo.local", "P@ssword123"),
            CancellationToken.None);
        var recovered = await users.GetByUsernameOrEmailAsync("locked-user", CancellationToken.None);
        Assert.False(string.IsNullOrWhiteSpace(login.AccessToken));
        Assert.Equal(0, recovered!.FailedLoginCount);
        Assert.Null(recovered.LockedUntil);
    }

    [Fact]
    public async Task Identity_MfaSetupLoginRecoveryAndDisableEnforceStepUpAuthentication()
    {
        var documents = new InMemoryDocumentRepository<UserDocument>();
        var users = new UserRepository(documents);
        var sessionDocuments = new InMemoryDocumentRepository<RefreshSessionDocument>();
        var service = new IdentityService(
            users,
            new RefreshSessionStore(sessionDocuments),
            new InMemoryDurableTransactionRunner(),
            new Pbkdf2PasswordHasher(),
            new JwtTokenIssuer(),
            Options.Create(new JwtOptions { SigningKey = "unit-test-signing-key-with-more-than-32-chars" }),
            Options.Create(new LoginSecurityOptions()),
            Options.Create(new IdentityBootstrapOptions()),
            Options.Create(new PasswordResetOptions()),
            new RecordingPasswordResetNotifier(),
            new PlainMfaSecretProtector(),
            new InMemoryDistributedLockProvider(),
            Options.Create(new DistributedLockOptions()),
            _clock,
            _currentUser);
        var registration = await service.RegisterAsync(
            new RegisterUserRequest("mfa-user", "mfa-user@zumbo.local", "P@ssword123", "org-1"),
            CancellationToken.None);
        _currentUser.UserId = registration.User.Id;
        _currentUser.OrganizationId = registration.User.OrganizationId;

        var setup = await service.BeginMfaSetupAsync(
            new BeginMfaSetupRequest("P@ssword123"),
            CancellationToken.None);
        var storedSetup = await users.GetByIdAsync(registration.User.Id, CancellationToken.None);
        Assert.NotEqual(setup.Secret, storedSetup!.PendingMfaSecretProtected);
        var code = TotpSecurity.GenerateCode(setup.Secret, _clock.UtcNow);
        var confirmed = await service.ConfirmMfaSetupAsync(
            new ConfirmMfaSetupRequest(code),
            CancellationToken.None);
        Assert.True(confirmed.Enabled);
        Assert.Equal(8, confirmed.RecoveryCodes.Count);
        await Assert.ThrowsAsync<UnauthorizedException>(() => service.RefreshAsync(
            new RefreshTokenRequest(registration.RefreshToken),
            CancellationToken.None));

        var required = await Assert.ThrowsAsync<AuthenticationChallengeException>(() => service.LoginAsync(
            new LoginRequest("mfa-user", "P@ssword123"),
            CancellationToken.None));
        Assert.Equal("MFA_REQUIRED", required.Code);
        var login = await service.LoginAsync(
            new LoginRequest("mfa-user", "P@ssword123", code),
            CancellationToken.None);
        Assert.False(string.IsNullOrWhiteSpace(login.AccessToken));

        var recoveryLogin = await service.LoginAsync(
            new LoginRequest("mfa-user", "P@ssword123", confirmed.RecoveryCodes.First()),
            CancellationToken.None);
        Assert.False(string.IsNullOrWhiteSpace(recoveryLogin.AccessToken));
        var status = await service.GetMfaStatusAsync(CancellationToken.None);
        Assert.Equal(7, status.RemainingRecoveryCodes);

        var disabled = await service.DisableMfaAsync(
            new DisableMfaRequest("P@ssword123", TotpSecurity.GenerateCode(setup.Secret, _clock.UtcNow)),
            CancellationToken.None);
        Assert.False(disabled.Enabled);
        var passwordOnly = await service.LoginAsync(
            new LoginRequest("mfa-user", "P@ssword123"),
            CancellationToken.None);
        Assert.False(string.IsNullOrWhiteSpace(passwordOnly.AccessToken));
    }

    [Fact]
    public async Task Identity_ApiKeyStoresOnlyHashAuthenticatesAndStopsAfterRevocation()
    {
        var hasher = new Pbkdf2PasswordHasher();
        var userDocuments = new InMemoryDocumentRepository<UserDocument>();
        var users = new UserRepository(userDocuments);
        var user = new UserDocument
        {
            Username = "api-key-user",
            Email = "api-key-user@zumbo.local",
            OrganizationId = "org-1",
            PasswordHash = hasher.Hash("P@ssword123"),
            CreatedAt = _clock.UtcNow
        };
        await users.AddAsync(user, CancellationToken.None);
        _currentUser.UserId = user.Id;
        _currentUser.OrganizationId = user.OrganizationId;
        var keyDocuments = new InMemoryDocumentRepository<ApiKeyDocument>();
        var keyStore = new ApiKeyStore(keyDocuments);
        var conflictingStore = new OneShotApiKeyConflictStore(keyStore);
        var audit = new RecordingIdentityAuditWriter();
        var service = new ApiKeyService(
            conflictingStore,
            users,
            hasher,
            new PlainMfaSecretProtector(),
            audit,
            _clock,
            _currentUser);

        await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(
            new CreateApiKeyRequest(
                "Invalid scope",
                "P@ssword123",
                null,
                _clock.UtcNow.AddDays(30),
                ["organization:admin"]),
            "api-key-invalid-scope",
            CancellationToken.None));

        var created = await service.CreateAsync(
            new CreateApiKeyRequest(
                "Build integration",
                "P@ssword123",
                null,
                _clock.UtcNow.AddDays(30),
                ["api:full"]),
            "api-key-create",
            CancellationToken.None);
        var stored = await keyDocuments.SelectAsync(x => x.Id == created.Id, CancellationToken.None);
        Assert.NotNull(stored);
        Assert.NotEqual(created.Key, stored!.KeyHash);
        Assert.DoesNotContain(created.Key, stored.KeyHash, StringComparison.Ordinal);
        var principal = await service.AuthenticateAsync(created.Key, CancellationToken.None);
        Assert.Equal(user.Id, principal!.UserId);
        Assert.Equal(["api:full"], principal.Scopes);
        var afterFirstUse = await keyDocuments.SelectAsync(x => x.Id == created.Id, CancellationToken.None);
        Assert.NotNull(afterFirstUse!.LastUsedAt);
        var firstUseVersion = afterFirstUse.Version;
        Assert.NotNull(await service.AuthenticateAsync(created.Key, CancellationToken.None));
        var afterThrottledUse = await keyDocuments.SelectAsync(x => x.Id == created.Id, CancellationToken.None);
        Assert.Equal(firstUseVersion, afterThrottledUse!.Version);
        var expiring = await service.CreateAsync(
            new CreateApiKeyRequest(
                "Expiring integration",
                "P@ssword123",
                null,
                _clock.UtcNow.AddHours(2),
                ["api:full"]),
            "api-key-expiry",
            CancellationToken.None);
        var expiredDocument = await keyDocuments.SelectAsync(x => x.Id == expiring.Id, CancellationToken.None);
        expiredDocument!.ExpiresAt = _clock.UtcNow.AddSeconds(-1);
        expiredDocument.ExpiresAtUtc = expiredDocument.ExpiresAt.UtcDateTime;
        Assert.True(await keyStore.ReplaceOwnedAsync(expiredDocument, CancellationToken.None));
        Assert.Null(await service.AuthenticateAsync(expiring.Key, CancellationToken.None));
        Assert.Contains("ApiKeyCreated", audit.Actions);

        conflictingStore.ConflictNextReplace();
        await service.RevokeAsync(created.Id, "api-key-revoke", CancellationToken.None);
        Assert.Null(await service.AuthenticateAsync(created.Key, CancellationToken.None));
        Assert.Contains("ApiKeyRevoked", audit.Actions);
    }

    private static async Task<(AuthResponse? Response, Exception? Error)> CaptureRefreshAsync(
        IdentityService service,
        string refreshToken)
    {
        try
        {
            return (await service.RefreshAsync(new RefreshTokenRequest(refreshToken), CancellationToken.None), null);
        }
        catch (Exception exception)
        {
            return (null, exception);
        }
    }

    [Fact]
    public async Task Identity_PrivacyExportAndAnonymizationRemoveCredentialsAndReferences()
    {
        var hasher = new Pbkdf2PasswordHasher();
        var documents = new InMemoryDocumentRepository<UserDocument>();
        var users = new UserRepository(documents);
        var user = new UserDocument
        {
            Username = "privacy-user",
            Email = "privacy-user@zumbo.local",
            OrganizationId = "org-privacy",
            PasswordHash = hasher.Hash("P@ssword123"),
            CreatedAt = _clock.UtcNow,
            MfaEnabled = true,
            MfaSecretProtected = "protected:SECRET"
        };
        var sessionDocuments = new InMemoryDocumentRepository<RefreshSessionDocument>();
        await sessionDocuments.CreateAsync(new RefreshSessionDocument
        {
            UserId = user.Id,
            OrganizationId = user.OrganizationId,
            TokenHash = "token-hash",
            CreatedAt = _clock.UtcNow,
            ExpiresAt = _clock.UtcNow.AddDays(1),
            ExpiresAtUtc = _clock.UtcNow.AddDays(1).UtcDateTime,
            RetainUntilUtc = _clock.UtcNow.AddDays(31).UtcDateTime
        }, CancellationToken.None);
        await users.AddAsync(user, CancellationToken.None);
        _currentUser.UserId = user.Id;
        _currentUser.OrganizationId = user.OrganizationId;
        var keyDocuments = new InMemoryDocumentRepository<ApiKeyDocument>();
        await keyDocuments.CreateAsync(new ApiKeyDocument
        {
            UserId = user.Id,
            OrganizationId = user.OrganizationId,
            Name = "Privacy key",
            KeyHash = "hash",
            CreatedAt = _clock.UtcNow,
            ExpiresAt = _clock.UtcNow.AddDays(1)
        });
        var processor = new RecordingPrivacyDataProcessor();
        var audit = new RecordingIdentityAuditWriter();
        var service = new PrivacyService(
            users,
            new RefreshSessionStore(sessionDocuments),
            new ApiKeyStore(keyDocuments),
            new InMemoryDurableTransactionRunner(),
            hasher,
            processor,
            audit,
            new InMemoryDistributedLockProvider(),
            Options.Create(new DistributedLockOptions()),
            _clock,
            _currentUser);

        var export = await service.ExportAsync(CancellationToken.None);
        Assert.Equal("privacy-user@zumbo.local", export.Profile.Email);
        Assert.Contains(export.Data, x => x.Category == "work-items");
        var anonymized = await service.AnonymizeAsync(
            new AnonymizeAccountRequest("P@ssword123", "ANONYMIZE"),
            "privacy-correlation",
            CancellationToken.None);

        var storedUser = await users.GetByIdAsync(user.Id, CancellationToken.None);
        var storedKey = await keyDocuments.SelectAsync(x => x.UserId == user.Id, CancellationToken.None);
        Assert.True(anonymized.Anonymized);
        Assert.StartsWith("anon-", storedUser!.Username, StringComparison.Ordinal);
        Assert.EndsWith("@invalid.local", storedUser.Email, StringComparison.Ordinal);
        Assert.False(storedUser.IsActive);
        Assert.False(storedUser.MfaEnabled);
        var storedSession = await sessionDocuments.SelectAsync(
            x => x.UserId == user.Id,
            CancellationToken.None);
        Assert.NotNull(storedSession!.RevokedAt);
        Assert.NotNull(storedKey!.RevokedAt);
        Assert.Equal(anonymized.Pseudonym, processor.Pseudonym);
        Assert.Contains("UserAnonymized", audit.Actions);
    }

    [Fact]
    public async Task Identity_RoleLifecycleUsesSecureBootstrapAndInvalidatesSessions()
    {
        var userDocuments = new InMemoryDocumentRepository<UserDocument>();
        var userRepository = new UserRepository(userDocuments);
        var sessionDocuments = new InMemoryDocumentRepository<RefreshSessionDocument>();
        var sessionStore = new RefreshSessionStore(sessionDocuments);
        var identity = new IdentityService(
            userRepository,
            sessionStore,
            new InMemoryDurableTransactionRunner(),
            new Pbkdf2PasswordHasher(),
            new JwtTokenIssuer(),
            Options.Create(new JwtOptions { SigningKey = "unit-test-signing-key-with-more-than-32-chars" }),
            Options.Create(new LoginSecurityOptions()),
            Options.Create(new IdentityBootstrapOptions
            {
                AdminEmails = ["admin@zumbo.local"],
                BootstrapToken = "bootstrap-secret"
            }),
            Options.Create(new PasswordResetOptions()),
            new RecordingPasswordResetNotifier(),
            new PlainMfaSecretProtector(),
            new InMemoryDistributedLockProvider(),
            Options.Create(new DistributedLockOptions()),
            _clock,
            _currentUser);
        await Assert.ThrowsAsync<ForbiddenException>(() => identity.RegisterAsync(
            new RegisterUserRequest("admin", "admin@zumbo.local", "P@ssword123", "org-1", "wrong"),
            CancellationToken.None));
        var admin = await identity.RegisterAsync(
            new RegisterUserRequest("admin", "admin@zumbo.local", "P@ssword123", "org-1", "bootstrap-secret"),
            CancellationToken.None);
        var member = await identity.RegisterAsync(
            new RegisterUserRequest("role-member", "role-member@zumbo.local", "P@ssword123", "org-1"),
            CancellationToken.None);
        _currentUser.UserId = admin.User.Id;
        _currentUser.OrganizationId = "org-1";
        _currentUser.Roles = ["User", "SystemAdmin"];
        var audit = new RecordingIdentityAuditWriter();
        var roleDocuments = new InMemoryDocumentRepository<IdentityRoleDocument>();
        var administration = new IdentityAdministrationService(
            userDocuments,
            roleDocuments,
            sessionStore,
            new InMemoryDurableTransactionRunner(),
            new IdentityPermissionService(userDocuments, roleDocuments, _currentUser),
            audit,
            new InMemoryDistributedLockProvider(),
            Options.Create(new DistributedLockOptions()),
            _clock,
            _currentUser);
        var role = await administration.CreateRoleAsync(
            new CreateRoleRequest("Release Manager", "org-1", ["Release.Approve", "Release.Publish"]),
            "test-correlation",
            CancellationToken.None);
        var memberBefore = await userDocuments.SelectAsync(x => x.Id == member.User.Id, CancellationToken.None);
        var oldStamp = memberBefore!.SecurityStamp;
        await administration.AssignRolesAsync(
            member.User.Id,
            new AssignUserRolesRequest(["Release Manager"]),
            "test-correlation",
            CancellationToken.None);
        var memberAfter = await userDocuments.SelectAsync(x => x.Id == member.User.Id, CancellationToken.None);

        Assert.Contains("SystemAdmin", admin.User.Roles);
        Assert.Contains("Release Manager", memberAfter!.Roles);
        Assert.NotEqual(oldStamp, memberAfter.SecurityStamp);
        var memberSessions = await sessionDocuments.ListByFilterAsync(
            x => x.UserId == member.User.Id,
            cancellationToken: CancellationToken.None);
        Assert.All(memberSessions, x => Assert.NotNull(x.RevokedAt));
        await Assert.ThrowsAsync<ConflictException>(() => administration.DeleteRoleAsync(
            role.Id, "test-correlation", CancellationToken.None));
        await administration.AssignRolesAsync(
            member.User.Id,
            new AssignUserRolesRequest(["User"]),
            "test-correlation",
            CancellationToken.None);
        await administration.DeleteRoleAsync(role.Id, "test-correlation", CancellationToken.None);
        var lastAdmin = await Assert.ThrowsAsync<ConflictException>(() => administration.AssignRolesAsync(
            admin.User.Id,
            new AssignUserRolesRequest(["User"]),
            "test-correlation",
            CancellationToken.None));
        Assert.Equal("LAST_SYSTEM_ADMIN", lastAdmin.Code);
        Assert.Contains(audit.Actions, x => x == "UserRolesChanged");
    }

    [Fact]
    public async Task WorkItem_FlowTimeReport_UsesCreationAndActiveWorkTimestamps()
    {
        var repository = new InMemoryDocumentRepository<WorkItemDocument>();
        var service = CreateWorkItemService(repository);
        _clock.UtcNow = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
        var item = await service.CreateAsync(
            new CreateWorkItemRequest("project-1", "board-1", "Measure flow", "Task", "High", "user-2", null),
            "test-correlation",
            CancellationToken.None);

        _clock.UtcNow = new DateTimeOffset(2026, 7, 2, 9, 0, 0, TimeSpan.Zero);
        await service.MoveAsync(item.Id, new MoveWorkItemRequest("In Progress"), "test-correlation", CancellationToken.None);
        await service.MoveAsync(item.Id, new MoveWorkItemRequest("Code Review"), "test-correlation", CancellationToken.None);
        await service.MoveAsync(item.Id, new MoveWorkItemRequest("Test"), "test-correlation", CancellationToken.None);
        _clock.UtcNow = new DateTimeOffset(2026, 7, 4, 9, 0, 0, TimeSpan.Zero);
        var completed = await service.MoveAsync(item.Id, new MoveWorkItemRequest("Done"), "test-correlation", CancellationToken.None);

        var report = await service.FlowTimeAsync(
            "project-1",
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 5),
            CancellationToken.None);

        Assert.Equal(5, completed.StatusHistory.Count);
        Assert.Equal(72, report.AverageLeadTimeHours);
        Assert.Equal(48, report.AverageCycleTimeHours);
        Assert.Equal(1, report.CycleTimeSampleSize);
    }

    [Fact]
    public async Task WorkItem_SubtaskRequiresValidParentOnSameBoard()
    {
        var service = CreateWorkItemService();

        await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(
            new CreateWorkItemRequest("project-1", "board-1", "Orphan", "Subtask", "Medium", null, null),
            "test-correlation",
            CancellationToken.None));

        var parent = await service.CreateAsync(
            new CreateWorkItemRequest("project-1", "board-1", "Parent", "Story", "Medium", null, null),
            "test-correlation",
            CancellationToken.None);
        var child = await service.CreateAsync(
            new CreateWorkItemRequest("project-1", "board-1", "Child", "sub-task", "Medium", null, null, parent.Id),
            "test-correlation",
            CancellationToken.None);

        Assert.Equal("Subtask", child.Type);
        Assert.Equal(parent.Id, child.ParentId);
    }

    [Fact]
    public async Task WorkItem_CommentEditPreservesRevisionAndRejectsInvalidInput()
    {
        var service = CreateWorkItemService();
        var item = await CreateAssignedWorkItemAsync(service, "Commented item");
        item = await service.AddCommentAsync(
            item.Id,
            new AddCommentRequest("  Original body  ", ["user-2", "user-2"]),
            "test-correlation",
            CancellationToken.None);
        item = await service.EditCommentAsync(
            item.Id,
            item.Comments.Single().Id,
            new EditCommentRequest("Updated body"),
            "test-correlation",
            CancellationToken.None);

        var comment = item.Comments.Single();
        Assert.Single(comment.Mentions);
        Assert.Equal("Original body", comment.History.Single().Body);
        Assert.Equal(_currentUser.UserId, comment.History.Single().EditedByUserId);
        Assert.NotNull(comment.EditedAt);

        await Assert.ThrowsAsync<ConflictException>(() => service.EditCommentAsync(
            item.Id,
            comment.Id,
            new EditCommentRequest("Updated body"),
            "test-correlation",
            CancellationToken.None));
        await Assert.ThrowsAsync<ValidationException>(() => service.AddCommentAsync(
            item.Id,
            new AddCommentRequest(new string('x', 10_001), []),
            "test-correlation",
            CancellationToken.None));

        var idempotent = await service.AddCommentAsync(
            item.Id,
            new AddCommentRequest("Automated note", [], "automation:run-1:0"),
            "automation-correlation",
            CancellationToken.None);
        idempotent = await service.AddCommentAsync(
            item.Id,
            new AddCommentRequest("Automated note", [], "automation:run-1:0"),
            "automation-correlation",
            CancellationToken.None);
        Assert.Equal(2, idempotent.Comments.Count);
        var reused = await Assert.ThrowsAsync<ConflictException>(() => service.AddCommentAsync(
            item.Id,
            new AddCommentRequest("Different note", [], "automation:run-1:0"),
            "automation-correlation",
            CancellationToken.None));
        Assert.Equal("COMMENT_IDEMPOTENCY_KEY_REUSED", reused.Code);
    }

    [Fact]
    public async Task WorkItem_ParentCannotCompleteOrArchiveWhileChildIsActive()
    {
        var service = CreateWorkItemService();
        var parent = await CreateAssignedWorkItemAsync(service, "Parent");
        await service.CreateAsync(
            new CreateWorkItemRequest("project-1", "board-1", "Child", "Subtask", "Medium", null, null, parent.Id),
            "test-correlation",
            CancellationToken.None);
        await MoveToTestAsync(service, parent.Id);

        var completionError = await Assert.ThrowsAsync<ConflictException>(() => service.MoveAsync(
            parent.Id, new MoveWorkItemRequest("Done"), "test-correlation", CancellationToken.None));
        var archiveError = await Assert.ThrowsAsync<ConflictException>(() => service.ArchiveAsync(
            parent.Id, "test-correlation", CancellationToken.None));

        Assert.Equal("WORK_ITEM_HAS_ACTIVE_CHILDREN", completionError.Code);
        Assert.Equal("WORK_ITEM_HAS_ACTIVE_CHILDREN", archiveError.Code);
    }

    [Fact]
    public async Task WorkItem_DependencyBlocksCompletionUntilBlockerIsDone()
    {
        var service = CreateWorkItemService();
        var blocker = await CreateAssignedWorkItemAsync(service, "Blocker");
        var blocked = await CreateAssignedWorkItemAsync(service, "Blocked");
        await service.LinkAsync(
            blocker.Id, new LinkWorkItemRequest(blocked.Id, "Blocks"), "test-correlation", CancellationToken.None);
        await MoveToTestAsync(service, blocked.Id);

        var error = await Assert.ThrowsAsync<ConflictException>(() => service.MoveAsync(
            blocked.Id, new MoveWorkItemRequest("Done"), "test-correlation", CancellationToken.None));
        Assert.Equal("WORK_ITEM_BLOCKED", error.Code);

        await MoveToTestAsync(service, blocker.Id);
        await service.MoveAsync(blocker.Id, new MoveWorkItemRequest("Done"), "test-correlation", CancellationToken.None);
        var completed = await service.MoveAsync(
            blocked.Id, new MoveWorkItemRequest("Done"), "test-correlation", CancellationToken.None);
        Assert.Equal("Done", completed.Status);
    }

    [Fact]
    public async Task WorkItem_ApprovalRequiresDifferentApproverAndIsConsumedByMove()
    {
        var service = CreateWorkItemService(requiresApprovalForDone: true);
        var item = await CreateAssignedWorkItemAsync(service, "Approval item");
        await MoveToTestAsync(service, item.Id);
        item = await service.RequestApprovalAsync(
            item.Id,
            new RequestWorkItemApprovalRequest("Done"),
            "test-correlation",
            CancellationToken.None);
        var approval = item.Approvals.Single();

        var missingApproval = await Assert.ThrowsAsync<ConflictException>(() => service.MoveAsync(
            item.Id, new MoveWorkItemRequest("Done"), "test-correlation", CancellationToken.None));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.DecideApprovalAsync(
            item.Id,
            approval.Id,
            new DecideWorkItemApprovalRequest(true, "Self approval"),
            "test-correlation",
            CancellationToken.None));

        _currentUser.UserId = "approver-1";
        item = await service.DecideApprovalAsync(
            item.Id,
            approval.Id,
            new DecideWorkItemApprovalRequest(true, "Reviewed"),
            "test-correlation",
            CancellationToken.None);
        _currentUser.UserId = "user-1";
        item = await service.MoveAsync(
            item.Id, new MoveWorkItemRequest("Done"), "test-correlation", CancellationToken.None);

        Assert.Equal("WORK_ITEM_APPROVAL_REQUIRED", missingApproval.Code);
        Assert.Equal("Done", item.Status);
        Assert.NotNull(item.Approvals.Single().ConsumedAt);
        Assert.Contains("approved", item.Labels);
    }

    [Fact]
    public async Task Workflow_CustomStatusesValidateGraphAndPersistApprovalRule()
    {
        var audit = new RecordingLifecycleAuditWriter();
        var service = new WorkflowService(
            new InMemoryDocumentRepository<WorkflowDefinitionDocument>(),
            new AllowWorkflowProjectAccessChecker(),
            new InMemoryDistributedLockProvider(),
            Options.Create(new DistributedLockOptions()),
            _clock,
            audit);
        var statuses = new[]
        {
            new WorkflowStatusRequest("Open", "Todo"),
            new WorkflowStatusRequest("Building", "InProgress"),
            new WorkflowStatusRequest("Quality Gate", "InProgress"),
            new WorkflowStatusRequest("Released", "Done")
        };
        var transitions = new[]
        {
            new WorkflowTransitionRequest("Open", "Building", false, false),
            new WorkflowTransitionRequest("Building", "Quality Gate", true, false),
            new WorkflowTransitionRequest(
                "Quality Gate",
                "Released",
                false,
                true,
                true,
                [new WorkflowAutomationRequest("AddLabel", "released")])
        };
        var workflow = await service.UpsertAsync(
            new CreateWorkflowRequest("project-1", transitions, statuses),
            CancellationToken.None);

        Assert.Contains(workflow.Statuses, x => x.Name == "Released" && x.Category == "Done");
        Assert.True(workflow.Transitions.Single(x => x.ToStatus == "Released").RequiresApproval);
        Assert.Contains(workflow.Transitions.Single(x => x.ToStatus == "Released").Automations, x =>
            x.Action == "AddLabel" && x.Value == "released");

        var invalidStatuses = statuses.Append(new WorkflowStatusRequest("Orphan", "InProgress")).ToArray();
        var error = await Assert.ThrowsAsync<ConflictException>(() => service.UpsertAsync(
            new CreateWorkflowRequest("project-1", transitions, invalidStatuses),
            CancellationToken.None));
        Assert.Equal("WORKFLOW_STATUS_UNREACHABLE", error.Code);
        Assert.Single(audit.Actions, x => x == "WorkflowUpdated");
    }

    [Fact]
    public async Task Workflow_DefaultGraphCanBeSavedWithoutLosingReachabilityThroughCycles()
    {
        var service = new WorkflowService(
            new InMemoryDocumentRepository<WorkflowDefinitionDocument>(),
            new AllowWorkflowProjectAccessChecker(),
            new InMemoryDistributedLockProvider(),
            Options.Create(new DistributedLockOptions()),
            _clock,
            new RecordingLifecycleAuditWriter());
        var workflow = await service.GetOrCreateDefaultAsync("project-default-roundtrip", CancellationToken.None);

        var saved = await service.UpsertAsync(
            new CreateWorkflowRequest(
                workflow.ProjectId,
                workflow.Transitions.Select(transition => new WorkflowTransitionRequest(
                    transition.FromStatus,
                    transition.ToStatus,
                    transition.RequiresAssignee,
                    transition.RequiresCompletedChecklist,
                    transition.RequiresApproval,
                    transition.Automations.Select(automation =>
                        new WorkflowAutomationRequest(automation.Action, automation.Value)).ToArray())).ToArray(),
                workflow.Statuses.Select(status => new WorkflowStatusRequest(status.Name, status.Category)).ToArray()),
            CancellationToken.None);

        Assert.Equal(workflow.Statuses.Count, saved.Statuses.Count);
        Assert.Equal(workflow.Transitions.Count, saved.Transitions.Count);
    }

    [Fact]
    public async Task WorkItem_CustomDoneCategoryDrivesCompletionAndReporting()
    {
        var service = CreateWorkItemService(workflowPolicy: new ReleasedWorkflowPolicy());
        var item = await CreateAssignedWorkItemAsync(service, "Custom done item");
        item = await service.MoveAsync(
            item.Id,
            new MoveWorkItemRequest("Released"),
            "test-correlation",
            CancellationToken.None);
        var summary = await service.ProjectSummaryAsync("project-1", CancellationToken.None);

        Assert.Equal("Released", item.Status);
        Assert.NotNull(item.CompletedAt);
        Assert.Equal(1, summary.Done);
    }

    [Fact]
    public async Task WorkItem_ProjectSummaryPagesPastRepositoryLimit()
    {
        var repository = new InMemoryDocumentRepository<WorkItemDocument>();
        for (var index = 0; index < 250; index++)
        {
            await repository.CreateAsync(new WorkItemDocument
            {
                Id = $"paged-item-{index:D3}",
                ProjectId = "project-1",
                BoardId = "board-1",
                ColumnId = "todo-column",
                Title = $"Paged item {index}",
                Status = "To Do",
                CreatedAt = _clock.UtcNow,
                UpdatedAt = _clock.UtcNow
            });
        }

        var service = CreateWorkItemService(repository);
        var summary = await service.ProjectSummaryAsync("project-1", CancellationToken.None);

        Assert.Equal(250, summary.Total);
    }

    [Fact]
    public async Task Reporting_LargeDatasetHasNoRepositoryPageTruncation()
    {
        var repository = new InMemoryDocumentRepository<WorkItemDocument>();
        var createdAt = new DateTimeOffset(2026, 7, 2, 8, 0, 0, TimeSpan.Zero);
        for (var index = 0; index < 650; index++)
        {
            await repository.CreateAsync(new WorkItemDocument
            {
                Id = $"large-open-{index:D4}",
                ProjectId = "project-1",
                BoardId = "board-1",
                ColumnId = "todo-column",
                TeamId = "team-1",
                AssigneeUserId = "user-2",
                Title = $"Large open item {index}",
                Status = "To Do",
                DueDate = _clock.UtcNow.AddDays(1),
                CreatedAt = createdAt,
                UpdatedAt = createdAt,
                WorkLogs = [new WorkLogDocument { UserId = "user-2", Hours = 1, CreatedAt = createdAt }]
            });
            await repository.CreateAsync(new WorkItemDocument
            {
                Id = $"large-done-{index:D4}",
                ProjectId = "project-1",
                BoardId = "board-1",
                ColumnId = "done-column",
                TeamId = "team-1",
                AssigneeUserId = "user-2",
                Title = $"Large completed item {index}",
                Status = "Done",
                CompletedAt = createdAt.AddDays(2),
                CreatedAt = createdAt,
                UpdatedAt = createdAt.AddDays(2),
                WorkLogs = [new WorkLogDocument { UserId = "user-2", Hours = 2, CreatedAt = createdAt }],
                StatusHistory =
                [
                    new WorkItemStatusHistoryDocument
                    {
                        FromStatus = "To Do",
                        ToStatus = "In Progress",
                        ChangedByUserId = "user-2",
                        ChangedAt = createdAt.AddHours(8)
                    },
                    new WorkItemStatusHistoryDocument
                    {
                        FromStatus = "In Progress",
                        ToStatus = "Done",
                        ChangedByUserId = "user-2",
                        ChangedAt = createdAt.AddDays(2)
                    }
                ]
            });
        }

        var service = CreateWorkItemService(repository);
        var from = new DateOnly(2026, 7, 1);
        var to = new DateOnly(2026, 7, 31);

        var risks = await service.DueDateRisksAsync("project-1", 14, CancellationToken.None);
        var flow = await service.FlowTimeAsync("project-1", from, to, CancellationToken.None);
        var completion = await service.CompletionRateAsync("project-1", from, to, CancellationToken.None);
        var team = Assert.Single(await service.TeamPerformanceAsync(
            "project-1", from, to, CancellationToken.None));

        Assert.Equal(650, risks.Count);
        Assert.Equal(650, flow.CompletedItems);
        Assert.Equal(650, flow.CycleTimeSampleSize);
        Assert.Equal(1300, completion.CreatedItems);
        Assert.Equal(650, completion.CompletedItems);
        Assert.Equal(1300, team.AssignedItems);
        Assert.Equal(650, team.CompletedItems);
        Assert.Equal(1950, team.LoggedHours);
    }

    [Fact]
    public async Task ReadModelSnapshot_RetriesVersionRaceAndExposesFreshness()
    {
        var cache = new InMemoryWorkItemReadModelCache();
        var factoryCalls = 0;
        var snapshot = await cache.GetOrCreateSnapshotAsync(
            "project-1",
            "race",
            TimeSpan.FromMinutes(1),
            async ct =>
            {
                factoryCalls++;
                if (factoryCalls == 1)
                {
                    await cache.InvalidateProjectAsync("project-1", ct);
                }
                return factoryCalls;
            },
            CancellationToken.None);

        Assert.Equal(2, factoryCalls);
        Assert.Equal(2, snapshot.Data);
        Assert.Equal(1, snapshot.SourceVersion);
        Assert.False(snapshot.Stale);
        Assert.True(snapshot.GeneratedAt <= DateTimeOffset.UtcNow);

        var unstableCache = new InMemoryWorkItemReadModelCache();
        var unstableCalls = 0;
        var stale = await unstableCache.GetOrCreateSnapshotAsync(
            "project-1",
            "unstable",
            TimeSpan.FromMinutes(1),
            async ct =>
            {
                unstableCalls++;
                await unstableCache.InvalidateProjectAsync("project-1", ct);
                return unstableCalls;
            },
            CancellationToken.None);

        Assert.Equal(2, unstableCalls);
        Assert.Equal(2, stale.Data);
        Assert.Equal(1, stale.SourceVersion);
        Assert.True(stale.Stale);
    }

    [Fact]
    public async Task WorkItem_CreateInvalidatesCachedProjectSummary()
    {
        var service = CreateWorkItemService();
        await CreateAssignedWorkItemAsync(service, "First cached item");
        var initial = await service.ProjectSummaryAsync("project-1", CancellationToken.None);

        await CreateAssignedWorkItemAsync(service, "Second cached item");
        var refreshed = await service.ProjectSummaryAsync("project-1", CancellationToken.None);

        Assert.Equal(1, initial.Total);
        Assert.Equal(2, refreshed.Total);
    }

    [Fact]
    public async Task Notification_OwnershipPreferencesAndEmailOutboxAreEnforced()
    {
        var notificationRepository = new InMemoryDocumentRepository<NotificationDocument>();
        var preferenceRepository = new InMemoryDocumentRepository<NotificationPreferenceDocument>();
        var emailSender = new RecordingEmailNotificationSender();
        var service = new NotificationService(
            notificationRepository,
            preferenceRepository,
            new AllowNotificationUserDirectory(),
            emailSender,
            Options.Create(new EmailNotificationOptions { Enabled = true }),
            new InMemoryDistributedLockProvider(),
            Options.Create(new DistributedLockOptions()),
            _clock,
            _currentUser);
        await service.NotifyAsync("user-1", "Assignment", "Assigned work", CancellationToken.None);
        var ownNotifications = await service.ListAsync("user-1", CancellationToken.None);
        var notification = ownNotifications.Single();

        _currentUser.UserId = "user-2";
        await Assert.ThrowsAsync<ForbiddenException>(() => service.ListAsync("user-1", CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(() => service.MarkAsReadAsync(notification.Id, CancellationToken.None));

        _currentUser.UserId = "user-1";
        await service.UpdatePreferencesAsync(
            new UpdateNotificationPreferencesRequest(true, true, ["Mention"]),
            CancellationToken.None);
        await service.NotifyAsync("user-1", "Mention", "Muted mention", CancellationToken.None);
        Assert.Single(await service.ListAsync("user-1", CancellationToken.None));

        var sent = await service.DispatchPendingEmailsAsync(10, CancellationToken.None);
        var stored = await notificationRepository.SelectAsync(x => x.Id == notification.Id, CancellationToken.None);
        Assert.Equal(1, sent);
        Assert.Single(emailSender.Recipients);
        Assert.Equal("Sent", stored!.EmailStatus);
    }

    [Fact]
    public async Task Notification_TwoWorkersClaimOnceAndDeadLetterCanBeReplayed()
    {
        var notifications = new InMemoryDocumentRepository<NotificationDocument>();
        var sender = new ToggleEmailNotificationSender { Fail = true };
        var service = new NotificationService(
            notifications,
            new InMemoryDocumentRepository<NotificationPreferenceDocument>(),
            new AllowNotificationUserDirectory(),
            sender,
            Options.Create(new EmailNotificationOptions
            {
                Enabled = true,
                MaxAttempts = 2,
                BaseRetrySeconds = 1,
                MaximumRetrySeconds = 2,
                LeaseSeconds = 30
            }),
            new InMemoryDistributedLockProvider(),
            Options.Create(new DistributedLockOptions()),
            _clock,
            _currentUser);
        await service.NotifyAsync(
            "user-1", "Assignment", "Assigned", CancellationToken.None, "shared-delivery");

        var firstRace = await Task.WhenAll(
            service.DispatchPendingEmailsAsync(10, CancellationToken.None, "worker-a"),
            service.DispatchPendingEmailsAsync(10, CancellationToken.None, "worker-b"));
        Assert.Equal(0, firstRace.Sum());
        Assert.Equal(1, sender.Attempts);
        _clock.UtcNow = _clock.UtcNow.AddSeconds(1);
        await service.DispatchPendingEmailsAsync(10, CancellationToken.None, "worker-c");

        var deadLetter = await notifications.SelectAsync(x => x.DeduplicationKey == "shared-delivery")
            ?? throw new InvalidOperationException();
        Assert.Equal(NotificationEmailStatuses.DeadLetter, deadLetter.EmailStatus);
        var metrics = await service.GetDeliveryMetricsAsync("org-1", CancellationToken.None);
        Assert.Equal(1, metrics.DeadLetter);
        Assert.True(await service.ReplayDeadLetterAsync("org-1", deadLetter.Id, CancellationToken.None));

        sender.Fail = false;
        Assert.Equal(1, await service.DispatchPendingEmailsAsync(
            10, CancellationToken.None, "worker-replay"));
        Assert.Equal(NotificationEmailStatuses.Sent,
            (await notifications.SelectAsync(x => x.Id == deadLetter.Id))!.EmailStatus);
    }

    [Fact]
    public async Task Notification_DailyDigestUsesTimeZoneScheduleAndGroupsMessages()
    {
        var notifications = new InMemoryDocumentRepository<NotificationDocument>();
        var sender = new RecordingEmailNotificationSender();
        var service = new NotificationService(
            notifications,
            new InMemoryDocumentRepository<NotificationPreferenceDocument>(),
            new AllowNotificationUserDirectory(),
            sender,
            Options.Create(new EmailNotificationOptions { Enabled = true }),
            new InMemoryDistributedLockProvider(),
            Options.Create(new DistributedLockOptions()),
            _clock,
            _currentUser);
        var preference = await service.UpdatePreferencesAsync(
            new UpdateNotificationPreferencesRequest(
                true, true, [], DeliveryMode: NotificationDeliveryModes.DailyDigest,
                TimeZoneId: "UTC", DigestHourLocal: 13),
            CancellationToken.None);
        Assert.Equal(NotificationDeliveryModes.DailyDigest, preference.DeliveryMode);
        Assert.Equal("UTC", preference.TimeZoneId);

        await service.NotifyAsync("user-1", "Assignment", "First", CancellationToken.None, "digest-1");
        await service.NotifyAsync("user-1", "Mention", "Second", CancellationToken.None, "digest-2");
        Assert.Equal(0, await service.DispatchPendingEmailsAsync(10, CancellationToken.None, "early"));
        _clock.UtcNow = new DateTimeOffset(2026, 7, 8, 13, 0, 0, TimeSpan.Zero);
        Assert.Equal(2, await service.DispatchPendingEmailsAsync(10, CancellationToken.None, "digest"));
        Assert.Single(sender.Recipients);
        Assert.Contains("First", Assert.Single(sender.Bodies));
        Assert.Contains("Second", Assert.Single(sender.Bodies));
    }

    [Fact]
    public async Task WorkItem_DueDateReminderIsIdempotentAndResetsWhenDueDateChanges()
    {
        var workItems = new InMemoryDocumentRepository<WorkItemDocument>();
        var notificationRepository = new InMemoryDocumentRepository<NotificationDocument>();
        var notificationService = new NotificationService(
            notificationRepository,
            new InMemoryDocumentRepository<NotificationPreferenceDocument>(),
            new AllowNotificationUserDirectory(),
            new RecordingEmailNotificationSender(),
            Options.Create(new EmailNotificationOptions()),
            new InMemoryDistributedLockProvider(),
            Options.Create(new DistributedLockOptions()),
            _clock,
            _currentUser);
        var service = CreateWorkItemService(workItems, notificationService: notificationService);
        var item = await service.CreateAsync(
            new CreateWorkItemRequest(
                "project-1", "board-1", "Due soon", "Task", "High", "user-2", _clock.UtcNow.AddHours(4)),
            "test-correlation",
            CancellationToken.None);

        _currentUser.UserId = null;
        Assert.Equal(1, await service.SendDueDateRemindersAsync(24, CancellationToken.None));
        Assert.Equal(0, await service.SendDueDateRemindersAsync(24, CancellationToken.None));
        _currentUser.UserId = "user-2";
        Assert.Single(
            await notificationService.ListAsync("user-2", CancellationToken.None),
            x => x.Type == "DueDateReminder");

        _currentUser.UserId = "user-1";
        await service.UpdateAsync(
            item.Id,
            new UpdateWorkItemRequest(null, null, null, _clock.UtcNow.AddHours(8)),
            "test-correlation",
            CancellationToken.None);
        _currentUser.UserId = null;
        Assert.Equal(1, await service.SendDueDateRemindersAsync(24, CancellationToken.None));
        _currentUser.UserId = "user-2";
        Assert.Equal(2, (await notificationService.ListAsync("user-2", CancellationToken.None))
            .Count(x => x.Type == "DueDateReminder"));
    }

    [Fact]
    public async Task Reporting_CompletionRateAndTeamPerformanceUseExplicitTeamAssignment()
    {
        var service = CreateWorkItemService();
        var completed = await service.CreateAsync(
            new CreateWorkItemRequest(
                "project-1", "board-1", "Completed team item", "Task", "High", "user-2", null, null, "team-1"),
            "test-correlation",
            CancellationToken.None);
        var open = await service.CreateAsync(
            new CreateWorkItemRequest(
                "project-1", "board-1", "Open team item", "Task", "Medium", "user-2", null, null, "team-1"),
            "test-correlation",
            CancellationToken.None);
        await service.AddWorkLogAsync(
            completed.Id,
            new AddWorkLogRequest("user-2", 3.5m, "Delivery"),
            CancellationToken.None);
        await MoveToTestAsync(service, completed.Id);
        await service.MoveAsync(
            completed.Id, new MoveWorkItemRequest("Done"), "test-correlation", CancellationToken.None);
        var date = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);

        var completionRate = await service.CompletionRateAsync(
            "project-1", date, date, CancellationToken.None);
        var team = (await service.TeamPerformanceAsync(
            "project-1", date, date, CancellationToken.None)).Single();

        Assert.Equal("team-1", open.TeamId);
        Assert.Equal(2, completionRate.CreatedItems);
        Assert.Equal(1, completionRate.CompletedItems);
        Assert.Equal(50, completionRate.CompletionRatePercent);
        Assert.Equal(2, team.AssignedItems);
        Assert.Equal(1, team.CompletedItems);
        Assert.Equal(50, team.CompletionRatePercent);
        Assert.Equal(3.5m, team.LoggedHours);
    }

    [Fact]
    public async Task WorkItem_DependencyRejectsCyclesSelfLinksAndCrossProjectLinks()
    {
        var service = CreateWorkItemService();
        var first = await CreateAssignedWorkItemAsync(service, "First");
        var second = await CreateAssignedWorkItemAsync(service, "Second");
        var third = await CreateAssignedWorkItemAsync(service, "Third");
        var external = await service.CreateAsync(
            new CreateWorkItemRequest("project-2", "board-2", "External", "Task", "Medium", null, null),
            "test-correlation",
            CancellationToken.None);
        await service.LinkAsync(
            first.Id, new LinkWorkItemRequest(second.Id, "Blocks"), "test-correlation", CancellationToken.None);
        await service.LinkAsync(
            third.Id, new LinkWorkItemRequest(second.Id, "Blocks"), "test-correlation", CancellationToken.None);

        var duplicate = await Assert.ThrowsAsync<ConflictException>(() => service.LinkAsync(
            second.Id, new LinkWorkItemRequest(first.Id, "BlockedBy"), "test-correlation", CancellationToken.None));
        var cycle = await Assert.ThrowsAsync<ConflictException>(() => service.LinkAsync(
            second.Id, new LinkWorkItemRequest(first.Id, "Blocks"), "test-correlation", CancellationToken.None));
        await Assert.ThrowsAsync<ValidationException>(() => service.LinkAsync(
            first.Id, new LinkWorkItemRequest(first.Id, "RelatesTo"), "test-correlation", CancellationToken.None));
        await Assert.ThrowsAsync<ValidationException>(() => service.LinkAsync(
            first.Id, new LinkWorkItemRequest(external.Id, "RelatesTo"), "test-correlation", CancellationToken.None));

        Assert.Equal("WORK_ITEM_DEPENDENCY_EXISTS", duplicate.Code);
        Assert.Equal("WORK_ITEM_DEPENDENCY_CYCLE", cycle.Code);
    }

    private async Task<WorkItemResponse> CreateAssignedWorkItemAsync(WorkItemService service, string title) =>
        await service.CreateAsync(
            new CreateWorkItemRequest("project-1", "board-1", title, "Task", "High", "user-2", null),
            "test-correlation",
            CancellationToken.None);

    private static async Task MoveToTestAsync(WorkItemService service, string id)
    {
        await service.MoveAsync(id, new MoveWorkItemRequest("In Progress"), "test-correlation", CancellationToken.None);
        await service.MoveAsync(id, new MoveWorkItemRequest("Code Review"), "test-correlation", CancellationToken.None);
        await service.MoveAsync(id, new MoveWorkItemRequest("Test"), "test-correlation", CancellationToken.None);
    }

    [Fact]
    public async Task Audit_QueryFiltersActionAndReportsNextPage()
    {
        var repository = new InMemoryDocumentRepository<AuditLogDocument>();
        var service = new AuditService(
            repository,
            _clock,
            _currentUser,
            new FixedAuditRequestContext(),
            new AllowAuditAccessChecker(),
            Options.Create(new AuditOptions
            {
                HashChainEnabled = true,
                IntegrityKey = "unit-test-audit-integrity-key-32-bytes-minimum",
                RetentionDays = 30,
                ExportMaxRecords = 10,
                RetentionBatchSize = 10
            }));
        await service.WriteAsync("WorkItemCreated", "WorkItem", "item-1", null, "Task", "c1", CancellationToken.None);
        await service.WriteAsync("WorkItemMoved", "WorkItem", "item-1", "To Do", "In Progress", "c2", CancellationToken.None);
        await service.WriteAsync("WorkItemMoved", "WorkItem", "item-1", "In Progress", "Done", "c3", CancellationToken.None);
        await service.WriteAsync(
            "WorkItemSecured",
            "WorkItem",
            "item-1",
            """{"password":"old-secret","name":"alpha"}""",
            """{"password":"new-secret","name":"beta"}""",
            "c4",
            CancellationToken.None);
        await repository.CreateAsync(new AuditLogDocument
        {
            Id = "foreign-audit",
            OrganizationId = "org-2",
            ActorUserId = "user-1",
            Action = "Foreign",
            EntityType = "WorkItem",
            EntityId = "item-2",
            CreatedAt = _clock.UtcNow
        });

        var result = await service.QueryAsync(
            new AuditLogQuery("user-1", "WorkItemMoved", null, null, null, null, 1, 1),
            CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Equal("WorkItemMoved", result.Items[0].Action);
        Assert.Equal("203.0.113.10", result.Items[0].IpAddress);
        Assert.Equal("Zumbo-Unit-Test/1.0", result.Items[0].UserAgent);
        Assert.True(result.HasNextPage);
        Assert.NotNull(result.NextCursor);
        Assert.Equal("org-1", result.Items[0].OrganizationId);

        var secondPage = await service.QueryAsync(
            new AuditLogQuery("user-1", "WorkItemMoved", null, null, null, null, PageSize: 1, Cursor: result.NextCursor),
            CancellationToken.None);
        Assert.Single(secondPage.Items);
        Assert.False(secondPage.HasNextPage);
        Assert.NotEqual(result.Items[0].Id, secondPage.Items[0].Id);

        var secured = await service.QueryAsync(
            new AuditLogQuery("user-1", "WorkItemSecured", null, null, null, null),
            CancellationToken.None);
        var securedRecord = Assert.Single(secured.Items);
        Assert.DoesNotContain("secret", securedRecord.OldValue, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(securedRecord.Changes!, change =>
            change.Field == "password" && change.Redacted && change.NewValue == "[REDACTED]");

        var ownTenant = await service.QueryAsync(
            new AuditLogQuery("user-1", null, null, null, null, null),
            CancellationToken.None);
        Assert.DoesNotContain(ownTenant.Items, item => item.OrganizationId == "org-2");
        Assert.Equal(4, (await service.ExportAsync(
            new AuditLogQuery(null, null, "WorkItem", "item-1", null, null),
            CancellationToken.None)).Count);
        var exportRecords = await service.ExportAsync(
            new AuditLogQuery(null, null, "WorkItem", "item-1", null, null),
            CancellationToken.None);
        await using var exportStream = new MemoryStream();
        await AuditService.WriteNdjsonAsync(exportRecords, exportStream, CancellationToken.None);
        var exportLines = System.Text.Encoding.UTF8.GetString(exportStream.ToArray())
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(exportRecords.Count, exportLines.Length);
        Assert.DoesNotContain("old-secret", string.Join('\n', exportLines), StringComparison.Ordinal);
        Assert.DoesNotContain("new-secret", string.Join('\n', exportLines), StringComparison.Ordinal);

        var integrity = await service.VerifyIntegrityAsync("org-1", CancellationToken.None);
        Assert.True(integrity.Valid);
        var tampered = await repository.SelectAsync(x => x.OrganizationId == "org-1" && x.ChainSequence == 1)
            ?? throw new InvalidOperationException();
        tampered.Action = "Tampered";
        await repository.ReplaceByFilterAsync(x => x.Id == tampered.Id, tampered);
        Assert.False((await service.VerifyIntegrityAsync("org-1", CancellationToken.None)).Valid);

        await repository.CreateAsync(new AuditLogDocument
        {
            Id = "expired-audit",
            OrganizationId = "org-1",
            ActorUserId = "user-1",
            Action = "Expired",
            EntityType = "WorkItem",
            EntityId = "item-1",
            CreatedAt = _clock.UtcNow.AddDays(-31)
        });
        var retention = await service.PurgeExpiredAsync("org-1", _clock.UtcNow, CancellationToken.None);
        Assert.Equal(1, retention.Deleted);

        await Assert.ThrowsAsync<ValidationException>(() => service.QueryAsync(
            new AuditLogQuery("user-1", null, "WorkItem", null, null, null),
            CancellationToken.None));
    }

    [Fact]
    public async Task Audit_DeduplicationIsTenantScopedAndMalformedCursorIsRejected()
    {
        var repository = new InMemoryDocumentRepository<AuditLogDocument>();
        var service = new AuditService(
            repository,
            _clock,
            _currentUser,
            new FixedAuditRequestContext(),
            new AllowAuditAccessChecker());
        var longDeduplicationKey = new string('d', 200);

        await service.WriteAsAsync(
            "user-1", "Changed", "WorkItem", "item-1", null, "one", "c1",
            new AuditRequestMetadata(null, null), _clock.UtcNow, longDeduplicationKey,
            CancellationToken.None);
        await service.WriteAsAsync(
            "user-1", "Changed", "WorkItem", "item-1", null, "duplicate", "c1",
            new AuditRequestMetadata(null, null), _clock.UtcNow, longDeduplicationKey,
            CancellationToken.None);

        _currentUser.OrganizationId = "org-2";
        await service.WriteAsAsync(
            "user-1", "Changed", "WorkItem", "item-2", null, "two", "c2",
            new AuditRequestMetadata(null, null), _clock.UtcNow, longDeduplicationKey,
            CancellationToken.None);

        Assert.Equal(2, await repository.CountByFilterAsync());
        Assert.Equal(1, await repository.CountByFilterAsync(x => x.OrganizationId == "org-1"));
        Assert.Equal(1, await repository.CountByFilterAsync(x => x.OrganizationId == "org-2"));
        var malformedCursor = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(
            $"{long.MaxValue}|audit"));
        await Assert.ThrowsAsync<ValidationException>(() => service.QueryAsync(
            new AuditLogQuery("user-1", null, null, null, null, null, Cursor: malformedCursor),
            CancellationToken.None));
    }

    [Fact]
    public async Task Audit_RetentionReportsAValidPartialHashChainAndStillDetectsTampering()
    {
        var repository = new InMemoryDocumentRepository<AuditLogDocument>();
        var service = new AuditService(
            repository,
            _clock,
            _currentUser,
            new FixedAuditRequestContext(),
            new AllowAuditAccessChecker(),
            Options.Create(new AuditOptions
            {
                HashChainEnabled = true,
                IntegrityKey = "unit-test-audit-integrity-key-32-bytes-minimum",
                RetentionDays = 30,
                RetentionBatchSize = 10
            }));

        await service.WriteAsAsync(
            "user-1", "Old", "WorkItem", "item-1", null, "old", "c1",
            new AuditRequestMetadata(null, null), _clock.UtcNow.AddDays(-31), null,
            CancellationToken.None);
        await service.WriteAsAsync(
            "user-1", "Current", "WorkItem", "item-1", "old", "current", "c2",
            new AuditRequestMetadata(null, null), _clock.UtcNow, null,
            CancellationToken.None);

        Assert.Equal(1, (await service.PurgeExpiredAsync("org-1", _clock.UtcNow, CancellationToken.None)).Deleted);
        var integrity = await service.VerifyIntegrityAsync("org-1", CancellationToken.None);
        Assert.True(integrity.Valid);
        Assert.False(integrity.CompleteHistory);
        Assert.Equal(2, integrity.FirstSequence);
        Assert.NotNull(integrity.AnchorHash);

        var retained = await repository.SelectAsync(x => x.OrganizationId == "org-1")
            ?? throw new InvalidOperationException();
        retained.NewValue = "tampered";
        await repository.ReplaceByFilterAsync(x => x.Id == retained.Id, retained);
        Assert.False((await service.VerifyIntegrityAsync("org-1", CancellationToken.None)).Valid);
    }

    [Fact]
    public async Task Project_MembershipLifecycle_EnforcesOwnerAndRoleRules()
    {
        var audit = new RecordingLifecycleAuditWriter();
        var service = new ProjectService(
            new InMemoryDocumentRepository<ProjectDocument>(),
            new AllowProjectMemberDirectory(),
            new AllowProjectTeamDirectory(),
            new EmptyProjectTeamUsageChecker(),
            audit,
            _clock,
            _currentUser);

        await Assert.ThrowsAsync<ForbiddenException>(() => service.CreateAsync(
            new CreateProjectRequest("org-1", "BAD", "Spoofed owner", "user-2"),
            CancellationToken.None));

        var project = await service.CreateAsync(
            new CreateProjectRequest("org-1", "PRJ", "Delivery", "user-1"),
            CancellationToken.None);
        project = await service.AddMemberAsync(
            project.Id,
            new AddProjectMemberRequest("user-2", "ProjectAdmin"),
            CancellationToken.None);

        _currentUser.UserId = "user-2";
        await Assert.ThrowsAsync<ForbiddenException>(() => service.AddMemberAsync(
            project.Id,
            new AddProjectMemberRequest("user-3", "ProjectAdmin"),
            CancellationToken.None));

        _currentUser.UserId = "user-1";
        project = await service.ChangeMemberRoleAsync(
            project.Id,
            "user-2",
            new ChangeProjectMemberRoleRequest("Viewer"),
            CancellationToken.None);
        project = await service.UpdateAsync(
            project.Id,
            new UpdateProjectRequest("Delivery Platform", "Private"),
            CancellationToken.None);

        Assert.Equal("Private", project.Visibility);
        Assert.Contains(project.Members, x => x.UserId == "user-2" && x.Role == "Viewer");

        _currentUser.UserId = "user-2";
        await Assert.ThrowsAsync<ForbiddenException>(() => service.UpdateAsync(
            project.Id,
            new UpdateProjectRequest("Unauthorized", "Internal"),
            CancellationToken.None));

        _currentUser.UserId = "user-1";
        project = await service.RemoveMemberAsync(project.Id, "user-2", CancellationToken.None);
        Assert.DoesNotContain(project.Members, x => x.UserId == "user-2");
        var ownerError = await Assert.ThrowsAsync<ConflictException>(() =>
            service.RemoveMemberAsync(project.Id, "user-1", CancellationToken.None));
        Assert.Equal("PROJECT_OWNER_CANNOT_BE_REMOVED", ownerError.Code);
        Assert.Contains("ProjectCreated", audit.Actions);
        Assert.Contains("ProjectMemberAdded", audit.Actions);
        Assert.Contains("ProjectMemberRoleChanged", audit.Actions);
        Assert.Contains("ProjectUpdated", audit.Actions);
        Assert.Contains("ProjectMemberRemoved", audit.Actions);
    }

    [Fact]
    public async Task Organization_DepartmentTreeAndMembershipEnforceTenantIntegrity()
    {
        var audit = new RecordingLifecycleAuditWriter();
        var service = new OrganizationService(
            new InMemoryDocumentRepository<OrganizationDocument>(),
            new AllowOrganizationMemberDirectory(),
            new InMemoryDistributedLockProvider(),
            Options.Create(new DistributedLockOptions()),
            _clock,
            _currentUser,
            audit);
        var organization = await service.CreateAsync(
            new CreateOrganizationRequest("Zumbo", "org-1"),
            CancellationToken.None);
        organization = await service.CreateDepartmentAsync(
            organization.Id,
            new CreateDepartmentRequest("Engineering", null),
            CancellationToken.None);
        var root = organization.Departments.Single();
        organization = await service.CreateDepartmentAsync(
            organization.Id,
            new CreateDepartmentRequest("Platform", root.Id),
            CancellationToken.None);
        var child = organization.Departments.Single(x => x.ParentDepartmentId == root.Id);

        var cycle = await Assert.ThrowsAsync<ConflictException>(() => service.UpdateDepartmentAsync(
            organization.Id,
            root.Id,
            new UpdateDepartmentRequest(root.Name, child.Id),
            CancellationToken.None));
        var hasChildren = await Assert.ThrowsAsync<ConflictException>(() => service.DeleteDepartmentAsync(
            organization.Id,
            root.Id,
            CancellationToken.None));
        organization = await service.AssignMemberAsync(
            organization.Id,
            child.Id,
            new AssignDepartmentMemberRequest("user-2", "Senior Engineer"),
            CancellationToken.None);
        var duplicateMember = await Assert.ThrowsAsync<ConflictException>(() => service.AssignMemberAsync(
            organization.Id,
            root.Id,
            new AssignDepartmentMemberRequest("user-2", "Engineer"),
            CancellationToken.None));

        Assert.Equal("DEPARTMENT_HIERARCHY_CYCLE", cycle.Code);
        Assert.Equal("DEPARTMENT_HAS_CHILDREN", hasChildren.Code);
        Assert.Equal("DEPARTMENT_MEMBER_EXISTS", duplicateMember.Code);
        Assert.Equal("Senior Engineer", organization.Departments.Single(x => x.Id == child.Id).Members.Single().Position);

        _currentUser.UserId = "user-3";
        var forbidden = await Assert.ThrowsAsync<ForbiddenException>(() => service.UpdateAsync(
            organization.Id,
            new UpdateOrganizationRequest("Unauthorized rename"),
            CancellationToken.None));
        Assert.Equal("Organization management permission is required.", forbidden.Message);
        Assert.Contains("OrganizationCreated", audit.Actions);
        Assert.Equal(2, audit.Actions.Count(x => x == "DepartmentCreated"));
        Assert.Contains("DepartmentMemberAssigned", audit.Actions);
    }

    [Fact]
    public async Task Organization_LifecycleOwnershipPaginationAndRetention_AreEnforced()
    {
        var audit = new RecordingLifecycleAuditWriter();
        var service = new OrganizationService(
            new InMemoryDocumentRepository<OrganizationDocument>(),
            new AllowOrganizationMemberDirectory(),
            new InMemoryDistributedLockProvider(),
            Options.Create(new DistributedLockOptions()),
            _clock,
            _currentUser,
            audit,
            lifecycleOptions: Options.Create(new OrganizationLifecycleOptions { ArchiveRetentionDays = 90 }));
        var organization = await service.CreateAsync(
            new CreateOrganizationRequest("Lifecycle", "org-1"),
            CancellationToken.None);

        var immutable = await Assert.ThrowsAsync<ConflictException>(() => service.UpdateAsync(
            organization.Id,
            new UpdateOrganizationRequest("Lifecycle", "org-renamed"),
            CancellationToken.None));
        Assert.Equal("TENANT_KEY_IMMUTABLE", immutable.Code);

        foreach (var index in Enumerable.Range(1, 3))
        {
            organization = await service.CreateDepartmentAsync(
                organization.Id,
                new CreateDepartmentRequest("Department " + index, null),
                CancellationToken.None);
            var department = organization.Departments.Single(item => item.Name == "Department " + index);
            organization = await service.AssignMemberAsync(
                organization.Id,
                department.Id,
                new AssignDepartmentMemberRequest("user-" + (index + 1), "Position " + index),
                CancellationToken.None);
        }

        var firstPage = await service.ListMembersAsync(organization.Id, null, 2, CancellationToken.None);
        var secondPage = await service.ListMembersAsync(
            organization.Id,
            firstPage.NextCursor,
            2,
            CancellationToken.None);
        Assert.Equal(["user-2", "user-3"], firstPage.Items.Select(item => item.UserId));
        Assert.Equal("user-3", firstPage.NextCursor);
        Assert.Equal("user-4", Assert.Single(secondPage.Items).UserId);
        Assert.Null(secondPage.NextCursor);

        organization = await service.TransferOwnershipAsync(
            organization.Id,
            new TransferOrganizationOwnershipRequest("user-2"),
            "ownership-correlation",
            CancellationToken.None);
        Assert.Equal("user-2", organization.OwnerUserId);

        var formerOwner = await Assert.ThrowsAsync<ForbiddenException>(() => service.SuspendAsync(
            organization.Id,
            new SuspendOrganizationRequest("maintenance"),
            "suspend-correlation",
            CancellationToken.None));
        Assert.Equal("Organization management permission is required.", formerOwner.Message);

        _currentUser.UserId = "user-2";
        organization = await service.SuspendAsync(
            organization.Id,
            new SuspendOrganizationRequest("maintenance"),
            "suspend-correlation",
            CancellationToken.None);
        Assert.Equal(OrganizationStatuses.Suspended, organization.Status);
        var inactive = await Assert.ThrowsAsync<ConflictException>(() => service.CreateDepartmentAsync(
            organization.Id,
            new CreateDepartmentRequest("Blocked", null),
            CancellationToken.None));
        Assert.Equal("ORGANIZATION_NOT_ACTIVE", inactive.Code);

        organization = await service.RestoreAsync(
            organization.Id,
            "restore-correlation",
            CancellationToken.None);
        organization = await service.ArchiveAsync(
            organization.Id,
            "archive-correlation",
            CancellationToken.None);
        Assert.Equal(OrganizationStatuses.Archived, organization.Status);
        Assert.Equal(_clock.UtcNow.AddDays(90), organization.RetainUntil);
        organization = await service.RestoreAsync(
            organization.Id,
            "restore-correlation-2",
            CancellationToken.None);
        organization = await service.ArchiveAsync(
            organization.Id,
            "archive-correlation-2",
            CancellationToken.None);
        _clock.UtcNow = organization.RetainUntil!.Value;
        var expired = await Assert.ThrowsAsync<ConflictException>(() => service.RestoreAsync(
            organization.Id,
            "expired-correlation",
            CancellationToken.None));
        Assert.Equal("ORGANIZATION_RETENTION_EXPIRED", expired.Code);
        Assert.True(organization.Version >= 12);
        Assert.Contains("OrganizationOwnershipTransferred", audit.Actions);
        Assert.Contains("OrganizationSuspended", audit.Actions);
        Assert.Contains("OrganizationArchived", audit.Actions);
        Assert.Contains("OrganizationRestored", audit.Actions);
    }

    [Fact]
    public async Task Team_InviteAndOwnershipLifecycle_EnforcesRecipientAndOwnerRules()
    {
        var audit = new RecordingLifecycleAuditWriter();
        var directory = new TestTeamUserDirectory(
        [
            new TeamUserDirectoryEntry("user-1", "owner@zumbo.local", "org-1", true),
            new TeamUserDirectoryEntry("user-2", "member@zumbo.local", "org-1", true),
            new TeamUserDirectoryEntry("user-3", "other@zumbo.local", "org-1", true),
            new TeamUserDirectoryEntry("user-4", "inactive@zumbo.local", "org-1", false)
        ]);
        var repository = new InMemoryDocumentRepository<TeamDocument>();
        var service = new TeamService(
            repository,
            directory,
            audit,
            _clock,
            _currentUser);
        var team = await service.CreateAsync(
            new CreateTeamRequest("org-1", "Platform", "user-1"),
            CancellationToken.None);
        team = await service.InviteAsync(
            team.Id,
            new InviteTeamMemberRequest("member@zumbo.local", "Member"),
            CancellationToken.None);
        var inviteToken = Assert.IsType<string>(team.InvitationToken);
        var persistedInvite = (await repository.SelectAsync(x => x.Id == team.Id))!
            .Members.Single(x => x.Email == "member@zumbo.local");
        Assert.NotNull(persistedInvite.InvitationTokenHash);
        Assert.DoesNotContain(inviteToken, persistedInvite.InvitationTokenHash, StringComparison.Ordinal);

        _currentUser.UserId = "user-3";
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.AcceptInviteAsync(team.Id, new TeamInviteTokenRequest(inviteToken), CancellationToken.None));

        _currentUser.UserId = "user-2";
        team = await service.AcceptInviteAsync(
            team.Id,
            new TeamInviteTokenRequest(inviteToken),
            CancellationToken.None);
        Assert.Contains(team.Members, x => x.UserId == "user-2" && x.Status == "Active");
        var reused = await Assert.ThrowsAsync<ConflictException>(() => service.AcceptInviteAsync(
            team.Id,
            new TeamInviteTokenRequest(inviteToken),
            CancellationToken.None));
        Assert.Equal("TEAM_INVITE_NOT_PENDING", reused.Code);

        _currentUser.UserId = "user-1";
        team = await service.ChangeMemberRoleAsync(
            team.Id,
            "user-2",
            new ChangeTeamMemberRoleRequest("Admin"),
            CancellationToken.None);
        team = await service.TransferOwnershipAsync(
            team.Id,
            new TransferTeamOwnershipRequest("user-2"),
            CancellationToken.None);
        Assert.Contains(team.Members, x => x.UserId == "user-1" && x.Role == "Admin");
        Assert.Contains(team.Members, x => x.UserId == "user-2" && x.Role == "Owner");

        await Assert.ThrowsAsync<ForbiddenException>(() => service.InviteAsync(
            team.Id,
            new InviteTeamMemberRequest("other@zumbo.local", "Admin"),
            CancellationToken.None));
        var ownerError = await Assert.ThrowsAsync<ConflictException>(() =>
            service.RemoveMemberAsync(team.Id, "user-2", CancellationToken.None));
        Assert.Equal("TEAM_LAST_OWNER", ownerError.Code);

        _currentUser.UserId = "user-2";
        team = await service.InviteAsync(
            team.Id,
            new InviteTeamMemberRequest("other@zumbo.local", "Member"),
            CancellationToken.None);
        var expiringToken = Assert.IsType<string>(team.InvitationToken);
        var expiringInviteId = team.Members.Single(x => x.Email == "other@zumbo.local" && x.Status == "Invited").Id;
        _clock.UtcNow = _clock.UtcNow.AddDays(8);
        _currentUser.UserId = "user-3";
        var expired = await Assert.ThrowsAsync<ConflictException>(() =>
            service.AcceptInviteAsync(team.Id, new TeamInviteTokenRequest(expiringToken), CancellationToken.None));
        Assert.Equal("TEAM_INVITE_EXPIRED", expired.Code);
        team = (await service.ListAsync("org-1", CancellationToken.None)).Single(x => x.Id == team.Id);
        Assert.Contains(team.Members, x => x.Id == expiringInviteId && x.Status == "Expired");

        _currentUser.UserId = "user-2";
        team = await service.InviteAsync(
            team.Id,
            new InviteTeamMemberRequest("other@zumbo.local", "Member"),
            CancellationToken.None);
        var declinedToken = Assert.IsType<string>(team.InvitationToken);
        var declinedInviteId = team.Members.Single(x => x.Email == "other@zumbo.local" && x.Status == "Invited").Id;
        _currentUser.UserId = "user-3";
        team = await service.DeclineInviteAsync(
            team.Id,
            new TeamInviteTokenRequest(declinedToken),
            CancellationToken.None);
        Assert.Contains(team.Members, x => x.Id == declinedInviteId && x.Status == "Declined");
        _currentUser.UserId = "user-2";
        var inactive = await Assert.ThrowsAsync<ConflictException>(() => service.InviteAsync(
            team.Id,
            new InviteTeamMemberRequest("inactive@zumbo.local", "Member"),
            CancellationToken.None));
        Assert.Equal("USER_INACTIVE", inactive.Code);
        Assert.Contains("TeamCreated", audit.Actions);
        Assert.Contains("TeamMemberInvited", audit.Actions);
        Assert.Contains("TeamInviteAccepted", audit.Actions);
        Assert.Contains("TeamMemberRoleChanged", audit.Actions);
        Assert.Contains("TeamOwnershipTransferred", audit.Actions);
        Assert.Contains("TeamInviteDeclined", audit.Actions);
    }

    private WorkItemService CreateWorkItemService(
        InMemoryDocumentRepository<WorkItemDocument>? repository = null,
        bool requiresApprovalForDone = false,
        IWorkflowPolicy? workflowPolicy = null,
        NotificationService? notificationService = null,
        IWorkItemRealtimePublisher? realtimePublisher = null,
        IWorkItemSearchIndex? searchIndex = null,
        int degradedFallbackMaxItems = 1_000)
    {
        var notifications = new NotificationService(
            new InMemoryDocumentRepository<NotificationDocument>(),
            new InMemoryDocumentRepository<NotificationPreferenceDocument>(),
            new AllowNotificationUserDirectory(),
            new RecordingEmailNotificationSender(),
            Options.Create(new EmailNotificationOptions()),
            new InMemoryDistributedLockProvider(),
            Options.Create(new DistributedLockOptions()),
            _clock,
            _currentUser);
        var search = searchIndex ?? new InMemoryWorkItemSearchIndex();
        var cache = new InMemoryWorkItemReadModelCache();
        var workItemRepository = repository ?? new InMemoryDocumentRepository<WorkItemDocument>();
        return new WorkItemService(
            workItemRepository,
            new DirectNotificationPublisher(notificationService ?? notifications),
            new NoOpWorkItemAuditPublisher(),
            _clock,
            _currentUser,
            new AllowPermissionChecker(),
            new AllowWorkItemTeamPolicy(),
            workflowPolicy ?? new TestWorkflowPolicy(requiresApprovalForDone),
            new TestBoardPlacementPolicy(),
            new InMemoryAttachmentStorage(),
            new InMemoryDistributedLockProvider(),
            Options.Create(new DistributedLockOptions()),
            search,
            new DirectWorkItemSearchPublisher(search),
            realtimePublisher ?? new NoOpWorkItemRealtimePublisher(),
            cache,
            new DirectCacheInvalidationPublisher(cache),
            Options.Create(new WorkItemReadModelCacheOptions()),
            new WorkItemActivityStore(
                new InMemoryDocumentRepository<WorkItemCommentActivityDocument>(),
                new InMemoryDocumentRepository<WorkItemCommentRevisionActivityDocument>(),
                new InMemoryDocumentRepository<WorkItemAttachmentActivityDocument>(),
                new InMemoryDocumentRepository<WorkItemWorkLogActivityDocument>(),
                new InMemoryDocumentRepository<WorkItemApprovalActivityDocument>(),
                new InMemoryDocumentRepository<WorkItemTimelineActivityDocument>()),
            new WorkItemGraphService(
                new InMemoryDocumentRepository<WorkItemRelationEdgeDocument>(),
                workItemRepository,
                Options.Create(new WorkItemGraphOptions()),
                _clock),
            searchOptions: Options.Create(new SearchOptions
            {
                DegradedFallbackMaxItems = degradedFallbackMaxItems
            }));
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 7, 8, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class FixedCurrentUser : ICurrentUser
    {
        public string? UserId { get; set; } = "user-1";
        public string? OrganizationId { get; set; } = "org-1";
        public IReadOnlyCollection<string> Roles { get; set; } = ["User"];
    }

    private sealed class AllowPermissionChecker : IProjectPermissionChecker
    {
        public Task<ProjectResourceAuthorization> EnsureCanAsync(
            string userId,
            string projectId,
            string permission,
            CancellationToken ct) =>
            Task.FromResult(new ProjectResourceAuthorization(projectId, "org-1", userId, "ProjectOwner", false));
    }

    private sealed class NoOpWorkItemRealtimePublisher : IWorkItemRealtimePublisher
    {
        public Task PublishAsync(WorkItemRealtimeChange change, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class NoOpWorkItemAuditPublisher : IWorkItemAuditPublisher
    {
        public Task WriteAsync(
            string action,
            string entityType,
            string entityId,
            string? oldValue,
            string? newValue,
            string correlationId,
            CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class DirectNotificationPublisher(NotificationService service)
        : IWorkItemNotificationPublisher
    {
        public Task NotifyAsync(
            string userId,
            string type,
            string message,
            CancellationToken ct,
            string? deduplicationKey = null) =>
            service.NotifyAsync(userId, type, message, ct, deduplicationKey);
    }

    private sealed class DirectWorkItemSearchPublisher(IWorkItemSearchIndex search)
        : IWorkItemSearchPublisher
    {
        public Task IndexAsync(WorkItemSearchRecord record, CancellationToken ct) =>
            search.IndexAsync(record, ct);

        public Task DeleteAsync(string workItemId, CancellationToken ct) =>
            search.DeleteAsync(workItemId, ct);
    }

    private sealed class UnavailableWorkItemSearchIndex : IWorkItemSearchIndex
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task IndexAsync(WorkItemSearchRecord record, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public Task DeleteAsync(string id, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<WorkItemSearchResult> SearchAsync(
            WorkItemSearchQuery query,
            CancellationToken cancellationToken = default) =>
            Task.FromException<WorkItemSearchResult>(new WorkItemSearchUnavailableException("offline"));
        public Task<WorkItemSearchRebuildResult> RebuildAsync(
            IReadOnlyCollection<WorkItemSearchRecord> records,
            CancellationToken cancellationToken = default) =>
            Task.FromException<WorkItemSearchRebuildResult>(new WorkItemSearchUnavailableException("offline"));
    }

    private sealed class DirectCacheInvalidationPublisher(IWorkItemReadModelCache cache)
        : IWorkItemCacheInvalidationPublisher
    {
        public Task InvalidateProjectAsync(string projectId, CancellationToken ct) =>
            cache.InvalidateProjectAsync(projectId, ct);
    }

    private sealed class RecordingWorkItemRealtimePublisher : IWorkItemRealtimePublisher
    {
        public List<WorkItemRealtimeChange> Changes { get; } = [];

        public Task PublishAsync(WorkItemRealtimeChange change, CancellationToken ct)
        {
            Changes.Add(change);
            return Task.CompletedTask;
        }
    }

    private sealed class AllowWorkItemTeamPolicy : IWorkItemTeamPolicy
    {
        public Task EnsureCanAssignAsync(
            string projectId,
            string teamId,
            string? assigneeUserId,
            CancellationToken ct) => Task.CompletedTask;

        public Task<IReadOnlyCollection<WorkItemTeamEntry>> ListProjectTeamsAsync(
            string projectId,
            CancellationToken ct) =>
            Task.FromResult<IReadOnlyCollection<WorkItemTeamEntry>>([new("team-1", "Platform")]);
    }

    private sealed class AllowBoardProjectAccessChecker : IBoardProjectAccessChecker
    {
        public Task EnsureCanAsync(string userId, string projectId, string permission, CancellationToken ct) =>
            Task.CompletedTask;
    }

    private sealed class EmptyBoardColumnUsageChecker : IBoardColumnUsageChecker
    {
        public Task<bool> HasWorkItemsAsync(string boardId, string columnId, string columnName, CancellationToken ct) =>
            Task.FromResult(false);

        public Task<bool> HasBoardWorkItemsAsync(string boardId, CancellationToken ct) => Task.FromResult(false);

        public Task ValidateMappingAsync(BoardDocument board, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class AllowAuditAccessChecker : IAuditAccessChecker
    {
        public Task<AuditReadScope> EnsureCanReadAsync(AuditLogQuery query, CancellationToken ct) =>
            Task.FromResult(new AuditReadScope("org-1"));
    }

    private sealed class RecordingIdentityAuditWriter : IIdentityAuditWriter
    {
        public List<string> Actions { get; } = [];

        public Task WriteAsync(
            string action,
            string entityId,
            string? oldValue,
            string? newValue,
            string correlationId,
            CancellationToken ct)
        {
            Actions.Add(action);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingLifecycleAuditWriter : ITeamAuditWriter, IProjectAuditWriter, IBoardAuditWriter, IWorkflowAuditWriter, IOrganizationAuditWriter
    {
        public List<string> Actions { get; } = [];

        public Task WriteAsync(
            string action,
            string entityId,
            string? oldValue,
            string? newValue,
            string correlationId,
            CancellationToken ct)
        {
            Actions.Add(action);
            return Task.CompletedTask;
        }

        public Task WriteAsync(
            string projectId,
            string? oldValue,
            string? newValue,
            string correlationId,
            CancellationToken ct)
        {
            Actions.Add("WorkflowUpdated");
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingPasswordResetNotifier : IPasswordResetNotifier
    {
        public List<(string Email, string Token, DateTimeOffset ExpiresAt)> Tokens { get; } = [];

        public Task SendAsync(string email, string rawToken, DateTimeOffset expiresAt, CancellationToken ct)
        {
            Tokens.Add((email, rawToken, expiresAt));
            return Task.CompletedTask;
        }
    }

    private sealed class PlainMfaSecretProtector : IMfaSecretProtector
    {
        public string Protect(string secret) => "protected:" + secret;

        public string Unprotect(string protectedSecret) => protectedSecret["protected:".Length..];
    }

    private sealed class CoordinatedRefreshSessionStore(IRefreshSessionStore inner) : IRefreshSessionStore
    {
        private TaskCompletionSource<bool>? pairReady;
        private int arrivals;

        public void CoordinateNextPair()
        {
            arrivals = 0;
            pairReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public async Task<RefreshSessionDocument?> GetByTokenAsync(string rawToken, CancellationToken ct)
        {
            var result = await inner.GetByTokenAsync(rawToken, ct);
            var barrier = pairReady;
            if (barrier is null)
            {
                return result;
            }

            if (Interlocked.Increment(ref arrivals) == 2)
            {
                pairReady = null;
                barrier.TrySetResult(true);
            }
            else
            {
                await barrier.Task.WaitAsync(ct);
            }

            return result;
        }

        public Task<RefreshSessionDocument?> GetByIdAsync(
            string sessionId,
            string userId,
            string organizationId,
            CancellationToken ct) =>
            inner.GetByIdAsync(sessionId, userId, organizationId, ct);

        public Task<IReadOnlyList<RefreshSessionDocument>> ListOwnedAsync(
            string userId,
            string organizationId,
            CancellationToken ct) =>
            inner.ListOwnedAsync(userId, organizationId, ct);

        public Task CreateAsync(RefreshSessionDocument session, CancellationToken ct) =>
            inner.CreateAsync(session, ct);

        public Task<bool> RevokeAsync(
            RefreshSessionDocument session,
            DateTimeOffset revokedAt,
            string? replacedBySessionId,
            CancellationToken ct) =>
            inner.RevokeAsync(session, revokedAt, replacedBySessionId, ct);

        public Task<int> RevokeAllAsync(
            string userId,
            string organizationId,
            DateTimeOffset revokedAt,
            CancellationToken ct) =>
            inner.RevokeAllAsync(userId, organizationId, revokedAt, ct);

        public Task<int> PurgeRetainedAsync(DateTimeOffset now, int batchSize, CancellationToken ct) =>
            inner.PurgeRetainedAsync(now, batchSize, ct);
    }

    private sealed class OneShotApiKeyConflictStore(IApiKeyStore inner) : IApiKeyStore
    {
        private bool conflictNextReplace;

        public void ConflictNextReplace() => conflictNextReplace = true;

        public Task CreateAsync(ApiKeyDocument apiKey, CancellationToken ct) => inner.CreateAsync(apiKey, ct);

        public Task<ApiKeyDocument?> GetByIdAsync(string apiKeyId, CancellationToken ct) =>
            inner.GetByIdAsync(apiKeyId, ct);

        public Task<ApiKeyDocument?> GetOwnedAsync(
            string apiKeyId,
            string userId,
            string organizationId,
            CancellationToken ct) =>
            inner.GetOwnedAsync(apiKeyId, userId, organizationId, ct);

        public Task<IReadOnlyList<ApiKeyDocument>> ListOwnedAsync(
            string userId,
            string organizationId,
            CancellationToken ct) =>
            inner.ListOwnedAsync(userId, organizationId, ct);

        public Task<IReadOnlyList<ApiKeyDocument>> ListAllOwnedAsync(
            string userId,
            string organizationId,
            CancellationToken ct) =>
            inner.ListAllOwnedAsync(userId, organizationId, ct);

        public async Task<bool> ReplaceOwnedAsync(ApiKeyDocument apiKey, CancellationToken ct)
        {
            if (conflictNextReplace)
            {
                conflictNextReplace = false;
                var concurrent = await inner.GetOwnedAsync(
                    apiKey.Id,
                    apiKey.UserId,
                    apiKey.OrganizationId,
                    ct);
                concurrent!.LastUsedAt = (concurrent.LastUsedAt ?? DateTimeOffset.UtcNow).AddSeconds(1);
                Assert.True(await inner.ReplaceOwnedAsync(concurrent, ct));
            }

            return await inner.ReplaceOwnedAsync(apiKey, ct);
        }

        public Task<int> PurgeExpiredAsync(DateTimeOffset now, int batchSize, CancellationToken ct) =>
            inner.PurgeExpiredAsync(now, batchSize, ct);
    }

    private sealed class RecordingPrivacyDataProcessor : IPrivacyDataProcessor
    {
        public string? Pseudonym { get; private set; }

        public Task<IReadOnlyCollection<PrivacyDataGroup>> ExportAsync(
            string userId,
            string organizationId,
            CancellationToken ct) =>
            Task.FromResult<IReadOnlyCollection<PrivacyDataGroup>>(
                [new PrivacyDataGroup("work-items", [new PrivacyDataReference("item-1", "assignee")], false)]);

        public async Task<long> WriteExportAsync(
            string userId,
            string organizationId,
            UserProfileResponse profile,
            Stream destination,
            CancellationToken ct)
        {
            await destination.WriteAsync("{}\n"u8.ToArray(), ct);
            return 1;
        }

        public Task EnsureCanAnonymizeAsync(string userId, string organizationId, CancellationToken ct) =>
            Task.CompletedTask;

        public Task AnonymizeReferencesAsync(
            string userId,
            string organizationId,
            string pseudonym,
            string username,
            string email,
            CancellationToken ct)
        {
            Pseudonym = pseudonym;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedAuditRequestContext : IAuditRequestContext
    {
        public AuditRequestMetadata GetMetadata() =>
            new("203.0.113.10", "Zumbo-Unit-Test/1.0");
    }

    private sealed class AllowProjectMemberDirectory : IProjectMemberDirectory
    {
        public Task EnsureEligibleAsync(string userId, string organizationId, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class AllowProjectTeamDirectory : IProjectTeamDirectory
    {
        public Task<ProjectTeamDirectoryEntry?> FindAsync(string teamId, CancellationToken ct) =>
            Task.FromResult<ProjectTeamDirectoryEntry?>(new ProjectTeamDirectoryEntry(teamId, "org-1", true));
    }

    private sealed class EmptyProjectTeamUsageChecker : IProjectTeamUsageChecker
    {
        public Task<bool> HasWorkItemsAsync(string projectId, string teamId, CancellationToken ct) =>
            Task.FromResult(false);
    }

    private sealed class AllowOrganizationMemberDirectory : IOrganizationMemberDirectory
    {
        public Task EnsureEligibleAsync(string userId, string organizationId, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class AllowWorkflowProjectAccessChecker : IWorkflowProjectAccessChecker
    {
        public Task EnsureCanViewAsync(string projectId, CancellationToken ct) => Task.CompletedTask;
        public Task EnsureCanManageAsync(string projectId, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class AllowNotificationUserDirectory : INotificationUserDirectory
    {
        public Task<NotificationUser?> FindAsync(string userId, CancellationToken ct) =>
            Task.FromResult<NotificationUser?>(new NotificationUser(userId, "org-1", userId + "@zumbo.local", true));
    }

    private sealed class RecordingEmailNotificationSender : IEmailNotificationSender
    {
        public List<string> Recipients { get; } = [];
        public List<string> Subjects { get; } = [];
        public List<string> Bodies { get; } = [];

        public Task SendAsync(string recipient, string subject, string body, CancellationToken ct)
        {
            Recipients.Add(recipient);
            Subjects.Add(subject);
            Bodies.Add(body);
            return Task.CompletedTask;
        }
    }

    private sealed class ToggleEmailNotificationSender : IEmailNotificationSender
    {
        public bool Fail { get; set; }
        public int Attempts { get; private set; }

        public Task SendAsync(string recipient, string subject, string body, CancellationToken ct)
        {
            Attempts++;
            if (Fail) throw new InvalidOperationException("Synthetic SMTP failure.");
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryAttachmentStorage : IAttachmentStorage
    {
        private readonly Dictionary<string, byte[]> _files = [];

        public async Task<StoredAttachment> SaveAsync(
            Stream content,
            string fileName,
            string contentType,
            long maxSizeBytes,
            CancellationToken ct)
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, ct);
            if (buffer.Length > maxSizeBytes)
            {
                throw new ValidationException("Attachment is too large.");
            }

            var key = Guid.NewGuid().ToString("N");
            _files[key] = buffer.ToArray();
            return new StoredAttachment(
                fileName,
                contentType,
                buffer.Length,
                key,
                Convert.ToHexString(SHA256.HashData(buffer.ToArray())));
        }

        public Task<StoredAttachment> ReprocessAsync(StoredAttachment attachment, CancellationToken ct) =>
            Task.FromResult(attachment with { SecurityState = AttachmentSecurityStates.Clean });

        public Task<Stream> OpenReadAsync(
            string storagePath,
            string contentType,
            string expectedChecksumSha256,
            CancellationToken ct) =>
            Task.FromResult<Stream>(new MemoryStream(_files[storagePath], writable: false));

        public Task<IReadOnlyList<StoredAttachmentObject>> ListObjectsAsync(int maxCount, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<StoredAttachmentObject>>([]);

        public Task DeleteAsync(string storagePath, CancellationToken ct)
        {
            _files.Remove(storagePath);
            return Task.CompletedTask;
        }
    }

    private sealed class TestTeamUserDirectory(IReadOnlyCollection<TeamUserDirectoryEntry> users) : ITeamUserDirectory
    {
        public Task<TeamUserDirectoryEntry?> FindByIdAsync(string userId, CancellationToken ct) =>
            Task.FromResult(users.SingleOrDefault(x => x.Id == userId));

        public Task<TeamUserDirectoryEntry?> FindByEmailAsync(string email, CancellationToken ct) =>
            Task.FromResult(users.SingleOrDefault(x => x.Email.Equals(email, StringComparison.OrdinalIgnoreCase)));
    }

    private sealed class TestWorkflowPolicy(bool requiresApprovalForDone = false) : IWorkflowPolicy
    {
        public Task<WorkflowTransitionRule> EnsureTransitionAllowedAsync(
            string projectId,
            string issueType,
            string fromStatus,
            string toStatus,
            CancellationToken ct)
        {
            var allowed = new[]
            {
                new WorkflowTransitionRule("To Do", "In Progress", false, false),
                new WorkflowTransitionRule("In Progress", "Code Review", true, false),
                new WorkflowTransitionRule("Code Review", "Test", true, false),
                new WorkflowTransitionRule(
                    "Test",
                    "Done",
                    false,
                    true,
                    requiresApprovalForDone,
                    requiresApprovalForDone ? [new WorkflowAutomationRule("AddLabel", "approved")] : [],
                    "Done")
            };

            var transition = allowed.SingleOrDefault(x =>
                x.FromStatus == fromStatus && x.ToStatus == toStatus);

            if (transition is null)
            {
                throw new ConflictException("WORKFLOW_TRANSITION_FORBIDDEN", "Transition is not allowed.");
            }

            return Task.FromResult(transition);
        }
    }

    private sealed class ReleasedWorkflowPolicy : IWorkflowPolicy
    {
        public Task<WorkflowTransitionRule> EnsureTransitionAllowedAsync(
            string projectId,
            string issueType,
            string fromStatus,
            string toStatus,
            CancellationToken ct) =>
            Task.FromResult(new WorkflowTransitionRule(
                fromStatus,
                toStatus,
                false,
                false,
                false,
                [],
                "Done"));
    }

    private sealed class TestBoardPlacementPolicy : IBoardPlacementPolicy
    {
        public Task<BoardPlacement> ResolveInitialAsync(string projectId, string boardId, CancellationToken ct) =>
            Task.FromResult(new BoardPlacement("column-todo", "To Do", false));

        public Task<BoardPlacement> EnsureCanMoveAsync(
            string projectId,
            string boardId,
            string workItemId,
            string targetStatus,
            CancellationToken ct) =>
            Task.FromResult(new BoardPlacement("column-" + targetStatus.ToLowerInvariant().Replace(' ', '-'), targetStatus, false));

        public Task EnsureHasCapacityAsync(
            string boardId,
            string columnId,
            string? ignoredWorkItemId,
            CancellationToken ct) => Task.CompletedTask;
    }
}
