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

    private static LambdaExpression UnwrapLambda(Expression expression)
    {
        expression = StripConvert(expression);
        if (expression is LambdaExpression lambda)
        {
            return lambda;
        }

        if (expression is UnaryExpression { Operand: LambdaExpression quoted })
        {
            return quoted;
        }

        throw new DocumentQueryException("The collection predicate is not a lambda expression.");
    }
}
