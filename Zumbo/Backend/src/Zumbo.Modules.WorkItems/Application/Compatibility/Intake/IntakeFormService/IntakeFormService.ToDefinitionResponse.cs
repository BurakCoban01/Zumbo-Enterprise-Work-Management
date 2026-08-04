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

    private static IntakeFormDefinitionResponse ToDefinitionResponse(
        IntakeFormDefinitionDocument source) => new(
        source.AccessPolicy,
        source.BoardId,
        source.WorkItemType,
        source.DefaultPriority,
        source.ConfirmationMessage,
        source.Fields.Select(ToFieldResponse).ToList(),
        new IntakeFieldMappingResponse(
            source.Mapping.TitleFieldKey,
            source.Mapping.DescriptionFieldKey,
            source.Mapping.PriorityFieldKey,
            source.Mapping.DueDateFieldKey,
            source.Mapping.CustomFields.Select(x => new IntakeCustomFieldMappingResponse(
                x.IntakeFieldKey,
                x.WorkItemFieldKey)).ToList()));
}
