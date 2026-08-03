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

    private string VisitPredicate(Expression expression, Scope scope)
    {
        expression = StripConvert(expression);
        if (!DependsOnParameter(expression))
        {
            return Evaluate(expression) is true ? "TRUE" : "FALSE";
        }

        return expression switch
        {
            BinaryExpression { NodeType: ExpressionType.AndAlso } binary =>
                $"({VisitPredicate(binary.Left, scope)} AND {VisitPredicate(binary.Right, scope)})",
            BinaryExpression { NodeType: ExpressionType.OrElse } binary =>
                $"({VisitPredicate(binary.Left, scope)} OR {VisitPredicate(binary.Right, scope)})",
            BinaryExpression binary when IsComparison(binary.NodeType) => VisitComparison(binary, scope),
            UnaryExpression { NodeType: ExpressionType.Not } unary => $"(NOT {VisitPredicate(unary.Operand, scope)})",
            MethodCallExpression call => VisitBooleanMethod(call, scope),
            MemberExpression member when NonNullable(member.Type) == typeof(bool) =>
                $"({VisitScalar(member, scope).Sql} IS TRUE)",
            ConstantExpression { Value: bool value } => value ? "TRUE" : "FALSE",
            _ => throw Unsupported(expression)
        };
    }
}
