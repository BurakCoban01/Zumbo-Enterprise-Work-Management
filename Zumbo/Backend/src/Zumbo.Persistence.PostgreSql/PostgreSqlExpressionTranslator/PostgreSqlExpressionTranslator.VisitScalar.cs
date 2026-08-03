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

    private SqlValue VisitScalar(Expression expression, Scope scope, Type? preferredType = null)
    {
        expression = StripConvert(expression);
        if (!DependsOnParameter(expression))
        {
            var value = Evaluate(expression);
            var type = preferredType ?? expression.Type;
            return new SqlValue(AddParameter(value, type), NonNullable(type));
        }

        if (expression == scope.Parameter && !scope.HasColumns)
        {
            return new SqlValue(CastJsonText($"{scope.JsonExpression} #>> '{{}}'", expression.Type), NonNullable(expression.Type));
        }

        if (expression is MemberExpression member)
        {
            if (member.Member.Name == nameof(Nullable<int>.Value)
                && Nullable.GetUnderlyingType(member.Expression!.Type) is not null)
            {
                return VisitScalar(member.Expression, scope, Nullable.GetUnderlyingType(member.Expression.Type));
            }

            if (member.Member.Name == nameof(Nullable<int>.HasValue)
                && Nullable.GetUnderlyingType(member.Expression!.Type) is not null)
            {
                var nullable = VisitScalar(member.Expression, scope);
                return new SqlValue($"({nullable.Sql} IS NOT NULL)", typeof(bool));
            }

            var path = GetMemberPath(member, scope.Parameter);
            if (scope.HasColumns && path.Count == 1 && path[0] == nameof(IDocument.Id))
            {
                return new SqlValue("id", typeof(string));
            }

            if (scope.HasColumns && path.Count == 1 && path[0] == nameof(IVersionedDocument.Version))
            {
                return new SqlValue("version", typeof(long));
            }

            var jsonText = JsonText(scope.JsonExpression, path);
            return new SqlValue(CastJsonText(jsonText, member.Type), NonNullable(member.Type));
        }

        if (expression is MethodCallExpression call
            && call.Object is not null
            && call.Arguments.Count == 0
            && call.Method.Name is nameof(string.ToLower) or nameof(string.ToLowerInvariant))
        {
            var operand = VisitScalar(call.Object, scope, typeof(string));
            return new SqlValue($"lower({operand.Sql})", typeof(string));
        }

        if (expression is MethodCallExpression stringCompare
            && stringCompare.Method.DeclaringType == typeof(string)
            && stringCompare.Method.Name is nameof(string.Compare) or nameof(string.CompareOrdinal)
            && stringCompare.Arguments.Count >= 2)
        {
            var left = VisitScalar(stringCompare.Arguments[0], scope, typeof(string));
            var right = VisitScalar(stringCompare.Arguments[1], scope, typeof(string));
            return new SqlValue(
                $"(CASE WHEN {left.Sql} < {right.Sql} THEN -1 WHEN {left.Sql} > {right.Sql} THEN 1 ELSE 0 END)",
                typeof(int));
        }

        if (expression is MethodCallExpression compareTo
            && compareTo.Object?.Type == typeof(string)
            && compareTo.Method.Name == nameof(string.CompareTo)
            && compareTo.Arguments.Count == 1)
        {
            var left = VisitScalar(compareTo.Object, scope, typeof(string));
            var right = VisitScalar(compareTo.Arguments[0], scope, typeof(string));
            return new SqlValue(
                $"(CASE WHEN {left.Sql} < {right.Sql} THEN -1 WHEN {left.Sql} > {right.Sql} THEN 1 ELSE 0 END)",
                typeof(int));
        }

        throw Unsupported(expression);
    }
}
