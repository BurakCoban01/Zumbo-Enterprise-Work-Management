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

    private string AddParameter(object? value, Type expectedType)
    {
        var name = $"p{parameters.Count}";
        value = NormalizeParameter(value, NonNullable(expectedType));
        parameters.Add(new NpgsqlParameter(name, value ?? DBNull.Value));
        return $"@{name}";
    }
}
