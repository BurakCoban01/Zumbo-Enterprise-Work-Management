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

    private static bool IsAny(MethodCallExpression call) =>
        call.Method.Name == nameof(Enumerable.Any)
        && call.Method.DeclaringType == typeof(Enumerable)
        && call.Arguments.Count is 1 or 2;
}
