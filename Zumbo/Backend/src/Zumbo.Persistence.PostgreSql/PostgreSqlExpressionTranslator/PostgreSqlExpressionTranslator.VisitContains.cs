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

    private string VisitContains(MethodCallExpression call, Scope scope)
    {
        Expression sourceExpression;
        Expression itemExpression;
        if (call.Object is not null)
        {
            sourceExpression = call.Object;
            itemExpression = call.Arguments[0];
        }
        else
        {
            sourceExpression = call.Arguments[0];
            itemExpression = call.Arguments[1];
        }

        if (!DependsOnParameter(sourceExpression))
        {
            if (Evaluate(sourceExpression) is not IEnumerable values)
            {
                throw Unsupported(call);
            }

            var item = VisitScalar(itemExpression, scope);
            var entries = values.Cast<object?>().ToList();
            if (entries.Count == 0)
            {
                return "FALSE";
            }

            var valueParameters = entries.Select(value => AddParameter(value, item.Type));
            return $"({item.Sql} IN ({string.Join(", ", valueParameters)}))";
        }

        var jsonSource = VisitJson(sourceExpression, scope);
        if (DependsOnParameter(itemExpression))
        {
            throw new DocumentQueryException("A document collection can only be searched for a captured scalar value.");
        }

        var itemValue = Evaluate(itemExpression);
        var json = JsonSerializer.Serialize(new[] { itemValue }, jsonOptions);
        var parameter = AddJsonParameter(json);
        return $"(COALESCE({jsonSource}, '[]'::jsonb) @> {parameter}::jsonb)";
    }
}
