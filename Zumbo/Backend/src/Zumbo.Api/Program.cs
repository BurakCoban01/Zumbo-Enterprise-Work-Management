var builder = WebApplication.CreateBuilder(args);

builder.AddZumboHost();
builder.Services
    .AddIdentityModule()
    .AddOrganizationsModule()
    .AddTeamsModule()
    .AddProjectsModule()
    .AddPortfolioModule()
    .AddGoalModule()
    .AddKnowledgeModule()
    .AddBoardsModule()
    .AddNotificationsModule(builder.Configuration)
    .AddAuditModule()
    .AddWorkflowsModule(builder.Configuration)
    .AddWorkItemsModule(builder.Configuration)
    .AddDashboardModule()
    .AddCapacityPlanningModule()
    .AddSprintsModule();

var app = builder.Build();

app.UseZumboPipeline();
app.MapZumboEndpoints();

app.Run();

public partial class Program;
