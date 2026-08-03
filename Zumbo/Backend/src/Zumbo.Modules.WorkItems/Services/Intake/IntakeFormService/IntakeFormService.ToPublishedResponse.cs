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

public sealed partial class IntakeFormService{

    internal static PublishedIntakeFormResponse ToPublishedResponse(
        IntakeFormVersionDocument source) => new(
        source.FormId,
        source.DefinitionVersion,
        source.Name,
        source.Description,
        source.Definition.AccessPolicy,
        source.Definition.ConfirmationMessage,
        source.Definition.Fields.Select(ToFieldResponse).ToList());
}
