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
        return predicateTranslator.VisitPredicate(expression, new PostgreSqlTranslationScope(scope.Parameter, scope.JsonExpression, scope.HasColumns));
    }

    private string VisitComparison(BinaryExpression expression, Scope scope)
    {
        return predicateTranslator.VisitComparison(expression, new PostgreSqlTranslationScope(scope.Parameter, scope.JsonExpression, scope.HasColumns));
    }

    private string VisitBooleanMethod(MethodCallExpression call, Scope scope)
    {
        return predicateTranslator.VisitBooleanMethod(call, new PostgreSqlTranslationScope(scope.Parameter, scope.JsonExpression, scope.HasColumns));
    }

    private string VisitStringEquals(MethodCallExpression call, Scope scope)
    {
        return predicateTranslator.VisitStringEquals(call, new PostgreSqlTranslationScope(scope.Parameter, scope.JsonExpression, scope.HasColumns));
    }

    private string VisitContains(MethodCallExpression call, Scope scope)
    {
        return predicateTranslator.VisitContains(call, new PostgreSqlTranslationScope(scope.Parameter, scope.JsonExpression, scope.HasColumns));
    }

    private string VisitAny(MethodCallExpression call, Scope scope)
    {
        return predicateTranslator.VisitAny(call, new PostgreSqlTranslationScope(scope.Parameter, scope.JsonExpression, scope.HasColumns));
    }

    private static bool IsComparison(ExpressionType type) => PostgreSqlExpressionUtilities.IsComparison(type);

    private static bool IsContains(MethodCallExpression call) => PostgreSqlExpressionUtilities.IsContains(call);

    private static bool IsAny(MethodCallExpression call) => PostgreSqlExpressionUtilities.IsAny(call);

    private static bool IsNull(Expression expression) => PostgreSqlExpressionUtilities.IsNull(expression);
}
