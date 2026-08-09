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
        return predicateTranslator.TranslatePredicate(expression);
    }

    public string TranslateOrder<TDocument>(Expression<Func<TDocument, object>> expression)
    {
        return predicateTranslator.TranslateOrder(expression);
    }
}
