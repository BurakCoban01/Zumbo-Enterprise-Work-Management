using Zumbo.Modules.Audit;
using Zumbo.Modules.Boards;
using Zumbo.Modules.Identity;
using Zumbo.Modules.Notifications;
using Zumbo.Modules.Organizations;
using Zumbo.Modules.Projects;
using Zumbo.Modules.Teams;
using Zumbo.Modules.Workflows;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.UnitTests;

public sealed class RepresentativeVerticalSliceValidatorTests
{
    [Fact]
    public void IdentitySlices_ValidateReadAndWriteRequests()
    {
        SearchUsersValidator.Validate(new SearchUsersQuery(null));

        var error = Assert.Throws<ValidationException>(() => RegisterUserValidator.Validate(
            new RegisterUserRequest("ab", "invalid", "weak", "")));

        Assert.Equal("VALIDATION_ERROR", error.Code);
        Assert.Equal("Username must be at least 3 characters.", error.Message);
    }

    [Fact]
    public void OrganizationSlices_ValidateReadAndWriteRequests()
    {
        ListOrganizationsValidator.Validate(new ListOrganizationsQuery());

        var error = Assert.Throws<ValidationException>(() =>
            CreateOrganizationValidator.Validate(new CreateOrganizationRequest("", "tenant")));

        Assert.Equal("Organization name and tenant key are required.", error.Message);
    }

    [Fact]
    public void TeamSlices_ValidateReadAndWriteRequests()
    {
        ListTeamsValidator.Validate(new ListTeamsQuery("tenant", false));

        var error = Assert.Throws<ValidationException>(() =>
            CreateTeamValidator.Validate(new CreateTeamRequest("tenant", "", "owner")));

        Assert.Equal("Organization id, team name and owner user id are required.", error.Message);
    }

    [Fact]
    public void ProjectSlices_ValidateReadAndWriteRequests()
    {
        ListProjectsValidator.Validate(new ListProjectsQuery("tenant", false));

        var error = Assert.Throws<ValidationException>(() =>
            CreateProjectValidator.Validate(new CreateProjectRequest("tenant", "", "Project", "owner")));

        Assert.Equal("Organization id, project key and name are required.", error.Message);
    }

    [Fact]
    public void BoardSlices_ValidateReadAndWriteRequests()
    {
        ListBoardsByProjectValidator.Validate(new ListBoardsByProjectQuery("project", false));

        var error = Assert.Throws<ValidationException>(() =>
            CreateBoardValidator.Validate(new CreateBoardRequest("project", "", "Kanban")));

        Assert.Equal("Project id and board name are required.", error.Message);
    }

    [Fact]
    public void WorkflowSlices_ValidateReadAndWriteRequests()
    {
        GetWorkflowValidator.Validate(new GetWorkflowQuery("project"));

        var error = Assert.Throws<ValidationException>(() =>
            UpsertWorkflowValidator.Validate(new CreateWorkflowRequest("project", [])));

        Assert.Equal("Project id and transitions are required.", error.Message);
    }

    [Fact]
    public void WorkItemSlices_ValidateReadAndWriteRequests()
    {
        var searchError = Assert.Throws<ValidationException>(() => SearchWorkItemsValidator.Validate(
            new WorkItemSearchRequest(null, null, null, null)));
        var createError = Assert.Throws<ValidationException>(() => CreateWorkItemValidator.Validate(
            new CreateWorkItemRequest("project", "board", "", "Task", "Medium", null, null)));

        Assert.Equal("Project id is required for work item search.", searchError.Message);
        Assert.Equal("Project id, board id and title are required.", createError.Message);
    }

    [Fact]
    public void NotificationSlices_ValidateReadAndWriteRequests()
    {
        MarkNotificationAsReadValidator.Validate(new MarkNotificationAsReadCommand("notification"));

        var error = Assert.Throws<ValidationException>(() => ListNotificationsValidator.Validate(
            new ListNotificationsQuery("user", 0, 50)));

        Assert.Equal("Notification page must be positive and page size must be between 1 and 100.", error.Message);
    }

    [Fact]
    public void AuditSlices_ValidateReadAndWriteRequests()
    {
        WriteAuditLogValidator.Validate(new WriteAuditLogCommand("Created", "Board", "id", null, null, "correlation"));

        var error = Assert.Throws<ValidationException>(() => QueryAuditLogValidator.ValidateAndNormalize(
            new AuditLogQuery(null, null, "Board", null, null, null)));

        Assert.Equal("Entity type and entity id must be provided together.", error.Message);
    }
}
