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
#pragma warning disable CS0414 // Retained baseline field for lossless refactor verification.
    private int jsonAliasIndex = 0;
#pragma warning restore CS0414
    private readonly PostgreSqlTranslationState translationState = new(jsonOptions);
    private PostgreSqlParameterCollector parameterCollector => translationState.ParameterCollector;
    private PostgreSqlPredicateTranslator predicateTranslator => translationState.PredicateTranslator;

    public IReadOnlyList<NpgsqlParameter> Parameters => parameterCollector.Parameters;
}
