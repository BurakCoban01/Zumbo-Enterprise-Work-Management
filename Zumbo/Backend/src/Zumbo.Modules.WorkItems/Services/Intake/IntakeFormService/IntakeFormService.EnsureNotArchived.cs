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

    private static void EnsureNotArchived(IntakeFormDocument form)
    {
        if (form.State == IntakeFormStates.Archived)
        {
            throw new ConflictException(
                "INTAKE_FORM_ARCHIVED",
                "Archived intake forms cannot be changed.");
        }
    }
}
