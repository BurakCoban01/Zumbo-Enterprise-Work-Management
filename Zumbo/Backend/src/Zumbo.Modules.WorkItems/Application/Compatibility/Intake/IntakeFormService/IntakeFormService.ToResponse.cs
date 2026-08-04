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

    internal static IntakeFormResponse ToResponse(IntakeFormDocument source) => new(
        source.Id,
        source.ProjectId,
        source.Name,
        source.Description,
        source.State,
        source.State == IntakeFormStates.Published
            && source.PublishedAccessPolicy == IntakeAccessPolicies.Public
                ? source.PublicId
                : null,
        source.PublishedVersion,
        ToDefinitionResponse(source.Draft),
        source.CreatedAt,
        source.UpdatedAt,
        source.PublishedAt,
        source.Version);
}
