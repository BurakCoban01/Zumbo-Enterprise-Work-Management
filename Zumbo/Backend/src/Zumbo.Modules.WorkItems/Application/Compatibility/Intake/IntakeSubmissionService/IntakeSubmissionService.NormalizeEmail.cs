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

public sealed partial class IntakeSubmissionService{

    private static string NormalizeEmail(string value)
    {
        try
        {
            var address = new MailAddress(value);
            if (!address.Address.Equals(value, StringComparison.OrdinalIgnoreCase))
            {
                throw new FormatException();
            }
            return address.Address.ToLowerInvariant();
        }
        catch (FormatException)
        {
            throw new ValidationException("Email field contains an invalid address.");
        }
    }
}
