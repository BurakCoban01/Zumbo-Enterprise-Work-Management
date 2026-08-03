using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemTypeSchemaService{

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_-]{0,39}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();
}
