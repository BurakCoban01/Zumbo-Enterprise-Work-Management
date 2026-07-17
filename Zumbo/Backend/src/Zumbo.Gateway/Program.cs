var builder = WebApplication.CreateBuilder(args);
GatewayHost.AddServices(builder);

var app = builder.Build();
GatewayHost.ConfigurePipeline(app);

app.Run();

public partial class Program;
