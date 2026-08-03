using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Zumbo.BuildingBlocks.Application.Messaging;
using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Persistence.PostgreSql;

public static class PostgreSqlPersistenceRegistration
{
    public static IServiceCollection AddZumboPostgreSql(
        this IServiceCollection services,
        Action<PostgreSqlPersistenceOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new PostgreSqlPersistenceOptions();
        configure(options);
        options.Validate();

        services.AddSingleton(options);
        services.AddSingleton(_ =>
        {
            var connection = new NpgsqlConnectionStringBuilder(options.ConnectionString)
            {
                Pooling = true,
                Timeout = options.ConnectionTimeoutSeconds,
                CommandTimeout = options.CommandTimeoutSeconds,
                MinPoolSize = options.MinimumPoolSize,
                MaxPoolSize = options.MaximumPoolSize
            };
            return new NpgsqlDataSourceBuilder(connection.ConnectionString).Build();
        });
        services.AddScoped<PostgreSqlSession>();
        services.TryAddScoped(typeof(IDocumentRepository<>), typeof(PostgreSqlDocumentRepository<>));
        services.TryAddScoped(typeof(IPostgreSqlRepository<>), typeof(PostgreSqlDocumentRepository<>));
        services.AddScoped<IPostgreSqlTransactionRunner, PostgreSqlTransactionRunner>();
        services.AddScoped<IDurableTransactionRunner>(provider =>
            (PostgreSqlTransactionRunner)provider.GetRequiredService<IPostgreSqlTransactionRunner>());
        services.AddScoped<IDurableEventOutbox, PostgreSqlDurableEventOutbox>();
        services.AddScoped<IDurableEventInbox, PostgreSqlDurableEventInbox>();
        services.AddSingleton<IPostgreSqlMigrationRunner, PostgreSqlMigrationRunner>();
        return services;
    }
}
