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

    private static object? Evaluate(Expression expression)
    {
        if (DependsOnParameter(expression))
        {
            throw new DocumentQueryException("A document-dependent expression cannot be evaluated as a SQL parameter.");
        }

        try
        {
            return Expression.Lambda<Func<object?>>(Expression.Convert(expression, typeof(object))).Compile().Invoke();
        }
        catch (Exception exception) when (exception is not DocumentQueryException)
        {
            throw new DocumentQueryException($"Expression value could not be evaluated: {expression}.", exception);
        }
    }
}
