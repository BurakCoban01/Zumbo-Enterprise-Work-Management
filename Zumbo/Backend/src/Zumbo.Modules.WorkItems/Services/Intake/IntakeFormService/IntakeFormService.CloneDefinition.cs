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

    internal static IntakeFormDefinitionDocument CloneDefinition(
        IntakeFormDefinitionDocument source) =>
        JsonSerializer.Deserialize<IntakeFormDefinitionDocument>(
            JsonSerializer.Serialize(source))
        ?? throw new InvalidOperationException("Intake form definition could not be cloned.");
}
