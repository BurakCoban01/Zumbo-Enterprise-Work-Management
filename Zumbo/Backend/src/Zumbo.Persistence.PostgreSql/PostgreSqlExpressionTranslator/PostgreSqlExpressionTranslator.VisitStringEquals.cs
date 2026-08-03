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

    private string VisitStringEquals(MethodCallExpression call, Scope scope)
    {
        var source = VisitScalar(call.Object!, scope);
        var argument = VisitScalar(call.Arguments[0], scope, typeof(string));
        if (call.Arguments.Count == 1)
        {
            return $"({source.Sql} = {argument.Sql})";
        }

        var comparison = Evaluate(call.Arguments[1]);
        return comparison switch
        {
            StringComparison.Ordinal => $"({source.Sql} = {argument.Sql})",
            StringComparison.OrdinalIgnoreCase => $"(lower({source.Sql}) = lower({argument.Sql}))",
            _ => throw new DocumentQueryException("Only ordinal string comparison can be translated to PostgreSQL.")
        };
    }
}
