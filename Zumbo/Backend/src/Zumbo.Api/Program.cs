var builder = WebApplication.CreateBuilder(args);

builder.AddZumboHost();
builder.Services
    .AddIdentityModule()
    .AddOrganizationsModule()
    .AddTeamsModule()
    .AddProjectsModule()
    .AddBoardsModule()
    .AddNotificationsModule(builder.Configuration)
    .AddAuditModule()
    .AddWorkflowsModule()
    .AddWorkItemsModule(builder.Configuration)
    .AddSprintsModule();

var app = builder.Build();

app.UseZumboPipeline();
app.MapZumboEndpoints();

app.Run();

public partial class Program;
