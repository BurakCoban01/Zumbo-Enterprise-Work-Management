using Microsoft.Extensions.Options;
using System.Text.Json;
using Zumbo.BuildingBlocks.Infrastructure.Concurrency;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.BuildingBlocks.Infrastructure.Search;
using Zumbo.BuildingBlocks.Infrastructure.Security;
using Zumbo.Modules.Audit;
using Zumbo.Modules.Boards;
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
            },
            moved =>
            {
                Assert.Equal("moved", moved.EventType);
                Assert.Equal("In Progress", moved.WorkItem.Status);
                Assert.Equal("move-correlation", moved.CorrelationId);
                Assert.True(JsonSerializer.SerializeToUtf8Bytes(moved).Length < 2_048);
            });
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
    public async Task Identity_RegisterAndLogin_UsesHashedPasswordAndTokens()
    {
        var repository = new InMemoryDocumentRepository<UserDocument>();
        var users = new UserRepository(repository);
        var service = new IdentityService(
            users,
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
        Assert.DoesNotContain(stored.RefreshTokens, x => x.TokenHash == registration.RefreshToken);
        Assert.Contains(stored.RefreshTokens, x => x.TokenHash == RefreshTokenSecurity.Hash(registration.RefreshToken));
        Assert.All(stored.RefreshTokens, x => Assert.False(string.IsNullOrWhiteSpace(x.SessionId)));
    }

    [Fact]
    public async Task Identity_PasswordResetIsOpaqueSingleUseAndRevokesSessions()
    {
        var documents = new InMemoryDocumentRepository<UserDocument>();
        var users = new UserRepository(documents);
        var notifier = new RecordingPasswordResetNotifier();
        var service = new IdentityService(
            users,
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
        var service = new IdentityService(
            users,
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
        var service = new IdentityService(
            users,
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
        var audit = new RecordingIdentityAuditWriter();
        var service = new ApiKeyService(
            keyDocuments,
            users,
            hasher,
            new PlainMfaSecretProtector(),
            audit,
            _clock,
            _currentUser);

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
        Assert.Contains("ApiKeyCreated", audit.Actions);

        await service.RevokeAsync(created.Id, "api-key-revoke", CancellationToken.None);
        Assert.Null(await service.AuthenticateAsync(created.Key, CancellationToken.None));
        Assert.Contains("ApiKeyRevoked", audit.Actions);
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
        user.RefreshTokens.Add(new RefreshTokenDocument
        {
            TokenHash = "token-hash",
            CreatedAt = _clock.UtcNow,
            ExpiresAt = _clock.UtcNow.AddDays(1)
        });
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
            keyDocuments,
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
        Assert.All(storedUser.RefreshTokens, x => Assert.NotNull(x.RevokedAt));
        Assert.NotNull(storedKey!.RevokedAt);
        Assert.Equal(anonymized.Pseudonym, processor.Pseudonym);
        Assert.Contains("UserAnonymized", audit.Actions);
    }

    [Fact]
    public async Task Identity_RoleLifecycleUsesSecureBootstrapAndInvalidatesSessions()
    {
        var userDocuments = new InMemoryDocumentRepository<UserDocument>();
        var userRepository = new UserRepository(userDocuments);
        var identity = new IdentityService(
            userRepository,
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
        Assert.All(memberAfter.RefreshTokens, x => Assert.NotNull(x.RevokedAt));
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
            new AllowAuditAccessChecker());
        await service.WriteAsync("WorkItemCreated", "WorkItem", "item-1", null, "Task", "c1", CancellationToken.None);
        await service.WriteAsync("WorkItemMoved", "WorkItem", "item-1", "To Do", "In Progress", "c2", CancellationToken.None);
        await service.WriteAsync("WorkItemMoved", "WorkItem", "item-1", "In Progress", "Done", "c3", CancellationToken.None);

        var result = await service.QueryAsync(
            new AuditLogQuery("user-1", "WorkItemMoved", null, null, null, null, 1, 1),
            CancellationToken.None);

        Assert.Single(result.Items);
        Assert.Equal("WorkItemMoved", result.Items[0].Action);
        Assert.Equal("203.0.113.10", result.Items[0].IpAddress);
        Assert.Equal("Zumbo-Unit-Test/1.0", result.Items[0].UserAgent);
        Assert.True(result.HasNextPage);

        var secondPage = await service.QueryAsync(
            new AuditLogQuery("user-1", "WorkItemMoved", null, null, null, null, 2, 1),
            CancellationToken.None);
        Assert.Single(secondPage.Items);
        Assert.False(secondPage.HasNextPage);

        await Assert.ThrowsAsync<ValidationException>(() => service.QueryAsync(
            new AuditLogQuery("user-1", null, "WorkItem", null, null, null),
            CancellationToken.None));
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
    public async Task Team_InviteAndOwnershipLifecycle_EnforcesRecipientAndOwnerRules()
    {
        var audit = new RecordingLifecycleAuditWriter();
        var directory = new TestTeamUserDirectory(
        [
            new TeamUserDirectoryEntry("user-1", "owner@zumbo.local", "org-1", true),
            new TeamUserDirectoryEntry("user-2", "member@zumbo.local", "org-1", true),
            new TeamUserDirectoryEntry("user-3", "other@zumbo.local", "org-1", true)
        ]);
        var service = new TeamService(
            new InMemoryDocumentRepository<TeamDocument>(),
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
        var inviteId = team.Members.Single(x => x.Email == "member@zumbo.local").Id;

        _currentUser.UserId = "user-3";
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.AcceptInviteAsync(team.Id, inviteId, CancellationToken.None));

        _currentUser.UserId = "user-2";
        team = await service.AcceptInviteAsync(team.Id, inviteId, CancellationToken.None);
        Assert.Contains(team.Members, x => x.UserId == "user-2" && x.Status == "Active");

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
        Assert.Equal("TEAM_OWNER_REMOVE_FORBIDDEN", ownerError.Code);

        _currentUser.UserId = "user-2";
        team = await service.InviteAsync(
            team.Id,
            new InviteTeamMemberRequest("other@zumbo.local", "Member"),
            CancellationToken.None);
        var expiringInviteId = team.Members.Single(x => x.Email == "other@zumbo.local" && x.Status == "Invited").Id;
        _clock.UtcNow = _clock.UtcNow.AddDays(8);
        _currentUser.UserId = "user-3";
        var expired = await Assert.ThrowsAsync<ConflictException>(() =>
            service.AcceptInviteAsync(team.Id, expiringInviteId, CancellationToken.None));
        Assert.Equal("TEAM_INVITE_EXPIRED", expired.Code);
        team = (await service.ListAsync("org-1", CancellationToken.None)).Single(x => x.Id == team.Id);
        Assert.Contains(team.Members, x => x.Id == expiringInviteId && x.Status == "Expired");

        _currentUser.UserId = "user-2";
        team = await service.InviteAsync(
            team.Id,
            new InviteTeamMemberRequest("other@zumbo.local", "Member"),
            CancellationToken.None);
        var rejectedInviteId = team.Members.Single(x => x.Email == "other@zumbo.local" && x.Status == "Invited").Id;
        _currentUser.UserId = "user-3";
        team = await service.RejectInviteAsync(team.Id, rejectedInviteId, CancellationToken.None);
        Assert.Contains(team.Members, x => x.Id == rejectedInviteId && x.Status == "Rejected");
        Assert.Contains("TeamCreated", audit.Actions);
        Assert.Contains("TeamMemberInvited", audit.Actions);
        Assert.Contains("TeamInviteAccepted", audit.Actions);
        Assert.Contains("TeamMemberRoleChanged", audit.Actions);
        Assert.Contains("TeamOwnershipTransferred", audit.Actions);
        Assert.Contains("TeamInviteRejected", audit.Actions);
    }

    private WorkItemService CreateWorkItemService(
        InMemoryDocumentRepository<WorkItemDocument>? repository = null,
        bool requiresApprovalForDone = false,
        IWorkflowPolicy? workflowPolicy = null,
        NotificationService? notificationService = null,
        IWorkItemRealtimePublisher? realtimePublisher = null)
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
        var audit = new AuditService(
            new InMemoryDocumentRepository<AuditLogDocument>(),
            _clock,
            _currentUser,
            new FixedAuditRequestContext(),
            new AllowAuditAccessChecker());
        return new WorkItemService(
            repository ?? new InMemoryDocumentRepository<WorkItemDocument>(),
            notificationService ?? notifications,
            audit,
            _clock,
            _currentUser,
            new AllowPermissionChecker(),
            new AllowWorkItemTeamPolicy(),
            workflowPolicy ?? new TestWorkflowPolicy(requiresApprovalForDone),
            new TestBoardPlacementPolicy(),
            new InMemoryAttachmentStorage(),
            new InMemoryDistributedLockProvider(),
            Options.Create(new DistributedLockOptions()),
            new InMemoryWorkItemSearchIndex(),
            realtimePublisher ?? new NoOpWorkItemRealtimePublisher(),
            new InMemoryWorkItemReadModelCache(),
            Options.Create(new WorkItemReadModelCacheOptions()));
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
        public Task EnsureCanAsync(string userId, string projectId, string permission, CancellationToken ct) =>
            Task.CompletedTask;
    }

    private sealed class NoOpWorkItemRealtimePublisher : IWorkItemRealtimePublisher
    {
        public Task PublishAsync(WorkItemRealtimeChange change, CancellationToken ct) => Task.CompletedTask;
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
    }

    private sealed class AllowAuditAccessChecker : IAuditAccessChecker
    {
        public Task EnsureCanReadAsync(AuditLogQuery query, CancellationToken ct) => Task.CompletedTask;
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

    private sealed class RecordingPrivacyDataProcessor : IPrivacyDataProcessor
    {
        public string? Pseudonym { get; private set; }

        public Task<IReadOnlyCollection<PrivacyDataGroup>> ExportAsync(
            string userId,
            string organizationId,
            CancellationToken ct) =>
            Task.FromResult<IReadOnlyCollection<PrivacyDataGroup>>(
                [new PrivacyDataGroup("work-items", [new PrivacyDataReference("item-1", "assignee")], false)]);

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
            Task.FromResult<NotificationUser?>(new NotificationUser(userId, userId + "@zumbo.local", true));
    }

    private sealed class RecordingEmailNotificationSender : IEmailNotificationSender
    {
        public List<string> Recipients { get; } = [];

        public Task SendAsync(string recipient, string subject, string body, CancellationToken ct)
        {
            Recipients.Add(recipient);
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
            return new StoredAttachment(fileName, contentType, buffer.Length, key);
        }

        public Task<Stream> OpenReadAsync(string storagePath, string contentType, CancellationToken ct) =>
            Task.FromResult<Stream>(new MemoryStream(_files[storagePath], writable: false));

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
