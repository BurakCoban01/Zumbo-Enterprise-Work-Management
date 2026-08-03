using Zumbo.BuildingBlocks.Application.Events;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record PreparedWorkItemTransition(string? ApprovalId) : ValueObject;
