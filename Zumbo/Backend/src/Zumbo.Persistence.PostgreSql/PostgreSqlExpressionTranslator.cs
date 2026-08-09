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

internal sealed partial class PostgreSqlExpressionTranslator(JsonSerializerOptions jsonOptions)
{
    private readonly List<NpgsqlParameter> parameters = [];
    private int jsonAliasIndex = 0;
    private readonly PostgreSqlTranslationState translationState = new(jsonOptions);
    private PostgreSqlParameterCollector parameterCollector => translationState.ParameterCollector;
    private PostgreSqlPredicateTranslator predicateTranslator => translationState.PredicateTranslator;
    private int CompatibilityJsonAliasIndex => jsonAliasIndex;

    public IReadOnlyList<NpgsqlParameter> Parameters => parameterCollector.Parameters;
}
