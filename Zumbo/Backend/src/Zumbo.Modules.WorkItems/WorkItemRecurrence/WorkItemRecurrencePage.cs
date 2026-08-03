using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed record WorkItemRecurrencePage(
    IReadOnlyCollection<WorkItemRecurrenceResponse> Items,
    int Page,
    int PageSize,
    long TotalCount);
