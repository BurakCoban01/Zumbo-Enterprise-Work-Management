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

    private static bool DependsOnParameter(Expression expression)
    {
        var visitor = new ParameterFindingVisitor();
        visitor.Visit(expression);
        return visitor.Found;
    }
}
