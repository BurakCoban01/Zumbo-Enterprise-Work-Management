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

    private static Expression StripConvert(Expression expression) => PostgreSqlExpressionUtilities.StripConvert(expression);

    private static object? Evaluate(Expression expression) => PostgreSqlExpressionUtilities.Evaluate(expression);

    private static bool DependsOnParameter(Expression expression) => PostgreSqlExpressionUtilities.DependsOnParameter(expression);

    private sealed class ParameterFindingVisitor : ExpressionVisitor
    {
        public bool Found { get; private set; }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            Found = true;
            return node;
        }
    }

    private static IReadOnlyList<string> GetMemberPath(MemberExpression member, ParameterExpression root) => PostgreSqlExpressionUtilities.GetMemberPath(member, root);

    private static LambdaExpression UnwrapLambda(Expression expression) => PostgreSqlExpressionUtilities.UnwrapLambda(expression);
}
