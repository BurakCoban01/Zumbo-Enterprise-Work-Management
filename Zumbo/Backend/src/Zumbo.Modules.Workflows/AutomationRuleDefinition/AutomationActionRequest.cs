using Zumbo.SharedKernel;

namespace Zumbo.Modules.Workflows;

public sealed record AutomationActionRequest(string Type, string? Value = null);
