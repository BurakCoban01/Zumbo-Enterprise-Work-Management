using System.Text.RegularExpressions;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

internal sealed record DevelopmentWorkItemReference(
    string ProjectKey,
    string WorkItemIdPrefix);
