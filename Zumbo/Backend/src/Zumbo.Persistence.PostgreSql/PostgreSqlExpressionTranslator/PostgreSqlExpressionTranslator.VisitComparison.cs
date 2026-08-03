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

    private string VisitComparison(BinaryExpression expression, Scope scope)
    {
        var leftIsNull = IsNull(expression.Left);
        var rightIsNull = IsNull(expression.Right);
        if (leftIsNull || rightIsNull)
        {
            if (leftIsNull && rightIsNull)
            {
                return expression.NodeType == ExpressionType.Equal ? "TRUE" : "FALSE";
            }

            if (expression.NodeType is not (ExpressionType.Equal or ExpressionType.NotEqual))
            {
                return "FALSE";
            }

            var operand = VisitScalar(leftIsNull ? expression.Right : expression.Left, scope).Sql;
            return expression.NodeType == ExpressionType.Equal
                ? $"({operand} IS NULL)"
                : $"({operand} IS NOT NULL)";
        }

        var left = VisitScalar(expression.Left, scope);
        var right = VisitScalar(expression.Right, scope, left.Type);
        var sqlOperator = expression.NodeType switch
        {
            ExpressionType.Equal => "=",
            ExpressionType.NotEqual => "<>",
            ExpressionType.GreaterThan => ">",
            ExpressionType.GreaterThanOrEqual => ">=",
            ExpressionType.LessThan => "<",
            ExpressionType.LessThanOrEqual => "<=",
            _ => throw Unsupported(expression)
        };
        return $"({left.Sql} {sqlOperator} {right.Sql})";
    }
}
