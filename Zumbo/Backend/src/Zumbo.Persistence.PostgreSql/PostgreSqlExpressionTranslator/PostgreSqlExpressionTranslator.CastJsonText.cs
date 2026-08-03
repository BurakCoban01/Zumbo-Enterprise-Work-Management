using System.Collections;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;
using NpgsqlTypes;
using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Persistence.PostgreSql;

internal sealed partial class PostgreSqlExpressionTranslator{

    private static string CastJsonText(string sql, Type type)
    {
        type = NonNullable(type);
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
}
