using System.Text;
using System.Text.Json;
using Zumbo.Persistence.PostgreSql;

namespace Zumbo.DatabaseMigrator;

internal static class MigrationCommandRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static async Task<int> RunAsync(MigratorCliOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        var migrator = new PostgreSqlSchemaMigrator(options.ConnectionString);
        try
        {
            switch (options.Command)
            {
                case MigratorCommand.Status:
                    var status = await migrator.StatusAsync(cancellationToken);
                    WriteJson(status);
                    break;
                case MigratorCommand.Apply:
                    var applied = await migrator.ApplyAsync(cancellationToken);
                    WriteJson(new { command = "apply", applied, succeeded = true });
                    break;
                case MigratorCommand.Rollback:
                    var rolledBack = await migrator.RollbackAsync(options.TargetVersion!.Value, cancellationToken);
                    WriteJson(new { command = "rollback", targetVersion = options.TargetVersion, rolledBack, succeeded = true });
                    break;
                case MigratorCommand.Script:
                    var script = await migrator.GenerateScriptAsync(
                        options.FromVersion,
                        options.ToVersion,
                        options.Idempotent,
                        cancellationToken);
                    await WriteScriptAsync(script, options.OutputPath, cancellationToken);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(options.Command));
            }

            return 0;
        }
        finally
        {
            if ((object)migrator is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else if ((object)migrator is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    private static void WriteJson<T>(T value) =>
        Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));

    private static async Task WriteScriptAsync(
        string script,
        string? outputPath,
        CancellationToken cancellationToken)
    {
        if (outputPath is null)
        {
            Console.Write(script);
            if (!script.EndsWith('\n'))
            {
                Console.WriteLine();
            }
            return;
        }

        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (directory is null || !Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Script output directory does not exist: '{directory}'.");
        }

        await using var stream = new FileStream(
            fullPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            useAsync: true);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await writer.WriteAsync(script.AsMemory(), cancellationToken);
        await writer.FlushAsync(cancellationToken);

        WriteJson(new { command = "script", output = fullPath, succeeded = true });
    }
}
