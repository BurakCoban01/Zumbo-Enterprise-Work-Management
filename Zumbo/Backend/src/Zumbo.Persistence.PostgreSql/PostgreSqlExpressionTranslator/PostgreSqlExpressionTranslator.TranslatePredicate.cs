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

    public string TranslatePredicate<TDocument>(Expression<Func<TDocument, bool>> expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        return VisitPredicate(expression.Body, new Scope(expression.Parameters[0], "document", HasColumns: true));
    }
}
