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

    private string VisitAny(MethodCallExpression call, Scope scope)
    {
        var sourceExpression = call.Arguments[0];
        var jsonSource = VisitJson(sourceExpression, scope);
        if (call.Arguments.Count == 1)
        {
            return $"(jsonb_array_length(COALESCE({jsonSource}, '[]'::jsonb)) > 0)";
        }

        var lambda = UnwrapLambda(call.Arguments[1]);
        var alias = $"j{jsonAliasIndex++}";
        var predicate = VisitPredicate(lambda.Body, new Scope(lambda.Parameters[0], $"{alias}.value", HasColumns: false));
        return $"EXISTS (SELECT 1 FROM jsonb_array_elements(COALESCE({jsonSource}, '[]'::jsonb)) AS {alias}(value) WHERE {predicate})";
    }
}
