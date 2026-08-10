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
        return parameterCollector.AddParameter(value, expectedType);
    }

    private string AddJsonParameter(string json)
    {
        return parameterCollector.AddJsonParameter(json);
    }

    public void AddParameters(NpgsqlCommand command)
    {
        parameterCollector.AddParameters(command);
    }

    private static object? NormalizeParameter(object? value, Type type)
    {
        return PostgreSqlParameterCollector.NormalizeParameter(value, type);
    }

    private static string PathArray(IEnumerable<string> path) => PostgreSqlJsonTranslation.PathArray(path);

    private readonly record struct Scope(ParameterExpression Parameter, string JsonExpression, bool HasColumns);
}
