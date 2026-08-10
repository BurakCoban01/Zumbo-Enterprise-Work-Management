using System.Text.Json;

namespace Zumbo.Persistence.PostgreSql;

internal sealed class PostgreSqlTranslationState
{
    internal PostgreSqlTranslationState(JsonSerializerOptions jsonOptions)
    {
        ParameterCollector = new PostgreSqlParameterCollector();
        PredicateTranslator = new PostgreSqlPredicateTranslator(jsonOptions, ParameterCollector, this);
    }

    internal PostgreSqlParameterCollector ParameterCollector { get; }
    internal PostgreSqlPredicateTranslator PredicateTranslator { get; }
    internal int JsonAliasIndex;
}
