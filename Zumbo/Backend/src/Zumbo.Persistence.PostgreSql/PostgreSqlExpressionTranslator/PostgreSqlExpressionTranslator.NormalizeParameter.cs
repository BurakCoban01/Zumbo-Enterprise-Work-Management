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

    private static object? NormalizeParameter(object? value, Type type)
    {
        if (value is null)
        {
            return null;
        }

        if (type.IsEnum)
        {
            return Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }

        if (value is DateTime dateTime)
        {
            return dateTime.Kind switch
            {
                DateTimeKind.Utc => dateTime,
                DateTimeKind.Local => dateTime.ToUniversalTime(),
                _ => DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
            };
        }

        if (value is Guid or Uri)
        {
            return value.ToString();
        }

        return value;
    }
}
