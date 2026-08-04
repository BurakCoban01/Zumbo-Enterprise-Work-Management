using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemTypeSchemaService{

    private static string Canonical(IReadOnlySet<string> supported, string? value, string description) =>
        supported.SingleOrDefault(item => item.Equals(value?.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? throw new ValidationException($"Unsupported {description}.");
}
