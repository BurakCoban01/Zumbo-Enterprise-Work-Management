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
    private int jsonAliasIndex;

    public IReadOnlyList<NpgsqlParameter> Parameters => parameters;
}
