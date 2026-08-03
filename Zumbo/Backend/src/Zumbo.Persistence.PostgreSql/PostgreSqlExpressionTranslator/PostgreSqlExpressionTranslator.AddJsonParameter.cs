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

    private string AddJsonParameter(string json)
    {
        var name = $"p{parameters.Count}";
        parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Jsonb) { Value = json });
        return $"@{name}";
    }
}
