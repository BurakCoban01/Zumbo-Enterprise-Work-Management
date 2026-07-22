using Zumbo.DatabaseMigrator;

try
{
    var options = MigratorCliOptions.Parse(args, Environment.GetEnvironmentVariable);
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cancellation.Cancel();
    };

    return await MigrationCommandRunner.RunAsync(options, cancellation.Token);
}
catch (MigratorUsageException exception)
{
    if (!string.IsNullOrWhiteSpace(exception.UsageError))
    {
        Console.Error.WriteLine(exception.UsageError);
    }
    Console.Error.WriteLine(MigratorUsageException.Usage);
    return exception.UsageError is null ? 0 : 2;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Migration command was cancelled.");
    return 130;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Migration command failed: {exception.Message}");
    return 1;
}
