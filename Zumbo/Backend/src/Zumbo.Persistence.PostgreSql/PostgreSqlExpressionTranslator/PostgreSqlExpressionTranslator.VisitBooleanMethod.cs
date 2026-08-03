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

    private string VisitBooleanMethod(MethodCallExpression call, Scope scope)
    {
        if (IsAny(call))
        {
            return VisitAny(call, scope);
        }

        if (call.Object?.Type == typeof(string))
        {
            var source = VisitScalar(call.Object, scope);
            var argument = VisitScalar(call.Arguments[0], scope, typeof(string));
            return call.Method.Name switch
            {
                nameof(string.Contains) => $"(strpos({source.Sql}, {argument.Sql}) > 0)",
                nameof(string.StartsWith) => $"(strpos({source.Sql}, {argument.Sql}) = 1)",
                nameof(string.EndsWith) =>
                    $"(right({source.Sql}, length({argument.Sql})) = {argument.Sql})",
                nameof(string.Equals) => VisitStringEquals(call, scope),
                _ => throw Unsupported(call)
            };
        }

        if (IsContains(call))
        {
            return VisitContains(call, scope);
        }

        if (call.Method.Name == nameof(object.Equals))
        {
            var leftExpression = call.Object ?? call.Arguments[0];
            var rightExpression = call.Object is null ? call.Arguments[1] : call.Arguments[0];
            var left = VisitScalar(leftExpression, scope);
            var right = VisitScalar(rightExpression, scope, left.Type);
            return $"({left.Sql} IS NOT DISTINCT FROM {right.Sql})";
        }

        throw Unsupported(call);
    }
}
