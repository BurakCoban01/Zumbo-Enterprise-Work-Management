using System.Globalization;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Runtime;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class IntakeFormService(
    IDocumentRepository<IntakeFormDocument> forms,
    IDocumentRepository<IntakeFormVersionDocument> versions,
    IDocumentRepository<IntakeSubmissionDocument> submissions,
    IProjectPermissionChecker permissions,
    IIntakeRoutePolicy routePolicy,
    IWorkItemAuditPublisher audit,
    IClock clock,
    ICurrentUser currentUser,
    IExpectedVersionAccessor? expectedVersions = null,
    IOptions<IntakeOptions>? configuredOptions = null)
{
    private static readonly Regex KeyPattern = new(
        "^[a-z][a-z0-9_-]{0,39}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private readonly ExpectedVersionState expectedVersion = new(expectedVersions);
    private readonly IntakeOptions options = configuredOptions?.Value ?? new IntakeOptions();
}
