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

    private string VisitJson(Expression expression, Scope scope)
    {
        expression = StripConvert(expression);
        if (expression == scope.Parameter && !scope.HasColumns)
        {
            return scope.JsonExpression;
        }

        if (expression is not MemberExpression member)
        {
            throw Unsupported(expression);
        }

        return JsonValue(scope.JsonExpression, GetMemberPath(member, scope.Parameter));
    }
}
