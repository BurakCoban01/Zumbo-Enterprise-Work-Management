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

    private IntakeFieldDefinitionDocument NormalizeField(IntakeFieldDefinitionRequest request)
    {
        var key = Required(request.Key, "Field key", 40).ToLowerInvariant();
        if (!KeyPattern.IsMatch(key))
        {
            throw new ValidationException(
                "Field keys must start with a letter and contain only lowercase letters, numbers, underscores or hyphens.");
        }

        var type = request.Type?.Trim() switch
        {
            IntakeFieldTypes.Text => IntakeFieldTypes.Text,
            IntakeFieldTypes.LongText => IntakeFieldTypes.LongText,
            IntakeFieldTypes.Email => IntakeFieldTypes.Email,
            IntakeFieldTypes.Number => IntakeFieldTypes.Number,
            IntakeFieldTypes.Date => IntakeFieldTypes.Date,
            IntakeFieldTypes.Choice => IntakeFieldTypes.Choice,
            IntakeFieldTypes.Checkbox => IntakeFieldTypes.Checkbox,
            IntakeFieldTypes.Attachment => IntakeFieldTypes.Attachment,
            _ => throw new ValidationException(
                "Field type must be Text, LongText, Email, Number, Date, Choice, Checkbox or Attachment.")
        };
        var fieldOptions = (request.Options ?? [])
            .Select(x => Required(x, "Choice option", 120))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (type == IntakeFieldTypes.Choice && fieldOptions.Count is < 1 or > 50)
        {
            throw new ValidationException("Choice fields require between 1 and 50 unique options.");
        }
        if (type != IntakeFieldTypes.Choice && fieldOptions.Count > 0)
        {
            throw new ValidationException("Only choice fields can define options.");
        }

        return new IntakeFieldDefinitionDocument
        {
            Key = key,
            Label = Required(request.Label, "Field label", 120),
            Type = type,
            Required = request.Required,
            HelpText = Optional(request.HelpText, 500),
            Options = fieldOptions
        };
    }
}
