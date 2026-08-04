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

public sealed partial class IntakeSubmissionService(
    IDocumentRepository<IntakeSubmissionDocument> submissions,
    IntakeFormService forms,
    IIntakeRoutePolicy routePolicy,
    IIntakeWorkItemCreator workItemCreator,
    IAttachmentStorage attachmentStorage,
    IWorkItemAuditPublisher audit,
    IClock clock,
    ICurrentUser currentUser,
    IOptions<IntakeOptions>? configuredOptions = null,
    ILogger<IntakeSubmissionService>? logger = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IntakeOptions options = configuredOptions?.Value ?? new IntakeOptions();
}
