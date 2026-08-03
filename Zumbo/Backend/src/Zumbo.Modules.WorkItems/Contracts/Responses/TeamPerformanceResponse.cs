using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Linq.Expressions;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Runtime;
using Zumbo.BuildingBlocks.Application.Search;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record TeamPerformanceResponse(
    string TeamId,
    string TeamName,
    int AssignedItems,
    int CompletedItems,
    double CompletionRatePercent,
    double? AverageLeadTimeHours,
    decimal LoggedHours);
