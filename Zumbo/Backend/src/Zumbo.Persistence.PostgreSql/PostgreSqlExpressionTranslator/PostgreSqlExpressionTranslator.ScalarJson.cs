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
        var value = predicateTranslator.VisitScalar(expression, new PostgreSqlTranslationScope(scope.Parameter, scope.JsonExpression, scope.HasColumns), preferredType);
        return new SqlValue(value.Sql, value.Type);
    }

    private string VisitJson(Expression expression, Scope scope)
    {
        return predicateTranslator.VisitJson(expression, new PostgreSqlTranslationScope(scope.Parameter, scope.JsonExpression, scope.HasColumns));
    }

    private static string CastJsonText(string sql, Type type) => PostgreSqlJsonTranslation.CastJsonText(sql, type);

    private static string JsonName(MemberInfo member) => PostgreSqlJsonTranslation.JsonName(member);

    private static string JsonText(string root, IReadOnlyList<string> path) => PostgreSqlJsonTranslation.JsonText(root, path);

    private static string JsonValue(string root, IReadOnlyList<string> path) => PostgreSqlJsonTranslation.JsonValue(root, path);

    private static Type NonNullable(Type type) => PostgreSqlExpressionUtilities.NonNullable(type);

    private readonly record struct SqlValue(string Sql, Type Type);
}
