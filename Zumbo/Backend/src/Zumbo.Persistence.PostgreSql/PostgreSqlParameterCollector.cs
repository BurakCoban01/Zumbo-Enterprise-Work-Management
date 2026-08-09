using System.Globalization;
using Npgsql;
using NpgsqlTypes;

namespace Zumbo.Persistence.PostgreSql;

internal sealed class PostgreSqlParameterCollector
{
    private readonly List<NpgsqlParameter> parameters = [];

    public IReadOnlyList<NpgsqlParameter> Parameters => parameters;

    public string AddParameter(object? value, Type expectedType)
    {
        var name = $"p{parameters.Count}";
        value = NormalizeParameter(value, Nullable.GetUnderlyingType(expectedType) ?? expectedType);
        parameters.Add(new NpgsqlParameter(name, value ?? DBNull.Value));
        return $"@{name}";
    }

    public string AddJsonParameter(string json)
    {
        var name = $"p{parameters.Count}";
        parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Jsonb) { Value = json });
        return $"@{name}";
    }

    public void AddParameters(NpgsqlCommand command)
    {
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(parameter);
        }
    }

    public static object? NormalizeParameter(object? value, Type type)
    {
        if (value is null)
        {
            return null;
        }

        if (type.IsEnum)
        {
            return Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }

        if (value is DateTime dateTime)
        {
            return dateTime.Kind switch
            {
                DateTimeKind.Utc => dateTime,
                DateTimeKind.Local => dateTime.ToUniversalTime(),
                _ => DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
            };
        }

        if (value is Guid or Uri)
        {
            return value.ToString();
        }

        return value;
    }
}
