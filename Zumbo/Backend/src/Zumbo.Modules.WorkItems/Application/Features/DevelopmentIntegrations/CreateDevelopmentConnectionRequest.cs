namespace Zumbo.Modules.WorkItems;

public sealed record CreateDevelopmentConnectionRequest(
    string Name,
    string Provider,
    string BaseUrl,
    string AccessToken);
