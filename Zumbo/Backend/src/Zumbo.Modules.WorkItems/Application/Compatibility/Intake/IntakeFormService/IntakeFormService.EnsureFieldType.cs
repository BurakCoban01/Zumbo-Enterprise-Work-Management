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

    private static void EnsureFieldType(
        IReadOnlyDictionary<string, IntakeFieldDefinitionDocument> fields,
        string key,
        string target,
        params string[] supportedTypes)
    {
        if (!fields.TryGetValue(key, out var field)
            || !supportedTypes.Contains(field.Type, StringComparer.Ordinal))
        {
            throw new ValidationException(
                $"The {target} mapping must reference a compatible intake field.");
        }
    }
}
