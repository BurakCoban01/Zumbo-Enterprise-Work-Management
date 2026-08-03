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

    private static IntakeFieldDefinitionResponse ToFieldResponse(
        IntakeFieldDefinitionDocument source) => new(
        source.Key,
        source.Label,
        source.Type,
        source.Required,
        source.HelpText,
        source.Options.ToList());
}
