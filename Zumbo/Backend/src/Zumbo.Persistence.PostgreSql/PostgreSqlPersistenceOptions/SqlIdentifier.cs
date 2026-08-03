using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Persistence.PostgreSql;

internal static partial class SqlIdentifier
{
    [GeneratedRegex("^[a-z][a-z0-9_]{0,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidIdentifier();

    public static void Validate(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || !ValidIdentifier().IsMatch(value))
        {
            throw new ArgumentException(
                "PostgreSQL identifiers must be lower-case ASCII names up to 63 characters.",
                parameterName);
        }
    }

    public static string Quote(string value)
    {
        Validate(value, nameof(value));
        return $"\"{value}\"";
    }
}
