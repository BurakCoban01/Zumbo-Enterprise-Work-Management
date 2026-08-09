using System.Reflection;
using System.Text.Json.Serialization;
using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Persistence.PostgreSql;

internal static class PostgreSqlJsonTranslation
{
    internal static string CastJsonText(string sql, Type type)
    {
        type = PostgreSqlExpressionUtilities.NonNullable(type);
        if (type == typeof(string) || type == typeof(char) || type == typeof(Guid) || type == typeof(Uri))
        {
            return sql;
        }

        if (type == typeof(DateTime) || type == typeof(DateTimeOffset))
        {
            return $"public.zumbo_parse_timestamptz({sql})";
        }

        var postgresType = type.IsEnum ? "bigint" : Type.GetTypeCode(type) switch
        {
            TypeCode.Boolean => "boolean",
            TypeCode.Byte or TypeCode.SByte or TypeCode.Int16 or TypeCode.UInt16
                or TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 => "bigint",
            TypeCode.UInt64 or TypeCode.Decimal => "numeric",
            TypeCode.Single or TypeCode.Double => "double precision",
            _ => throw new DocumentQueryException($"Member type {type.FullName} cannot be translated to PostgreSQL.")
        };
        return $"({sql})::{postgresType}";
    }

    internal static string JsonName(MemberInfo member) =>
        member.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? member.Name;

    internal static string JsonText(string root, IReadOnlyList<string> path) =>
        $"{root} #>> {PathArray(path)}";

    internal static string JsonValue(string root, IReadOnlyList<string> path) =>
        $"{root} #> {PathArray(path)}";

    internal static string PathArray(IEnumerable<string> path) =>
        $"ARRAY[{string.Join(", ", path.Select(segment => $"'{segment.Replace("'", "''", StringComparison.Ordinal)}'"))}]";
}
