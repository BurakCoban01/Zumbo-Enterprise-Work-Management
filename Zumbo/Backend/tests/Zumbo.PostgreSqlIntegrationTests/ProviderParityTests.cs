using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Persistence.PostgreSql;
using Zumbo.RepositoryContracts;

namespace Zumbo.PostgreSqlIntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class ProviderParityTests(PostgreSqlFixture fixture)
{
    [Fact]
    public void PostgreSql_ReusesTheExactTwelveTestRepositoryContract()
    {
        var contractTests = typeof(DocumentRepositoryContract)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method => method.GetCustomAttributes(typeof(FactAttribute), inherit: true).Length > 0)
            .Select(method => method.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(12, contractTests.Length);
        Assert.Contains(nameof(DocumentRepositoryContract.CompareExchange_IncrementsVersionAndRejectsStaleWriter), contractTests);
        Assert.Contains(nameof(DocumentRepositoryContract.CompareExchange_AllowsOnlyOneParallelWriter), contractTests);
        Assert.True(typeof(DocumentRepositoryContract).IsAssignableFrom(
            typeof(PostgreSqlDocumentRepositoryContractTests)));
    }

    [Fact]
    public void MongoAndPostgreSql_ImplementTheSameApplicationOwnedRepositoryApi()
    {
        var postgreSqlRepository = fixture.Api.CreateRepository<RepositoryContractDocument>(
            PostgreSqlFixture.TestSchema,
            PostgreSqlFixture.RepositoryTable);
        var applicationContract = typeof(Zumbo.BuildingBlocks.Application.Persistence.IDocumentRepository<>);

        Assert.Contains(postgreSqlRepository.GetType().GetInterfaces(), IsApplicationRepository);
        Assert.Contains(typeof(MongoRepository<>).GetInterfaces(), IsApplicationRepository);
        Assert.Equal("Zumbo.BuildingBlocks.Application", applicationContract.Assembly.GetName().Name);

        static bool IsApplicationRepository(Type candidate) =>
            candidate.IsGenericType
            && candidate.GetGenericTypeDefinition()
                == typeof(Zumbo.BuildingBlocks.Application.Persistence.IDocumentRepository<>);
    }

    [Fact]
    public void ProviderSelection_DoesNotChangeApplicationOrModuleSourceDependencies()
    {
        var sourceRoot = FindSourceRoot();
        var ownedProjects = Directory.GetDirectories(sourceRoot, "Zumbo.Modules.*")
            .Append(Path.Combine(sourceRoot, "Zumbo.BuildingBlocks.Application"));
        var forbiddenTokens = new[]
        {
            "Microsoft.EntityFrameworkCore",
            "MongoDB.",
            "Npgsql",
            "Zumbo.Persistence.PostgreSql"
        };

        var violations = ownedProjects
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                           && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                           || path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .SelectMany(path => forbiddenTokens
                .Where(token => File.ReadAllText(path).Contains(token, StringComparison.Ordinal))
                .Select(token => $"{Path.GetRelativePath(sourceRoot, path)} -> {token}"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        var applicationReferences = typeof(Zumbo.BuildingBlocks.Application.Persistence.IDocumentRepository<>).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name is not null
                           && (name.StartsWith("MongoDB.", StringComparison.Ordinal)
                               || name.StartsWith("Npgsql", StringComparison.Ordinal)))
            .ToArray();

        Assert.Empty(violations);
        Assert.Empty(applicationReferences);
    }

    [Fact]
    public void ComposeProfile_IsEphemeralHealthyAndLoopbackOnly()
    {
        var backendRoot = Directory.GetParent(FindSourceRoot())!.FullName;
        var compose = File.ReadAllText(Path.Combine(backendRoot, "docker-compose.postgresql.test.yml"));

        Assert.Contains("profiles: [\"postgresql-test\"]", compose, StringComparison.Ordinal);
        Assert.Contains("host_ip: 127.0.0.1", compose, StringComparison.Ordinal);
        Assert.Contains("${ZUMBO_POSTGRES_TEST_PORT:-0}", compose, StringComparison.Ordinal);
        Assert.Contains("tmpfs:", compose, StringComparison.Ordinal);
        Assert.Contains("healthcheck:", compose, StringComparison.Ordinal);
        Assert.Contains("pg_isready", compose, StringComparison.Ordinal);
        Assert.DoesNotContain("volumes:", compose, StringComparison.Ordinal);
        Assert.DoesNotContain("0.0.0.0", compose, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnmappedDocumentType_FailsFastInsteadOfGuessingFromItsNamespace()
    {
        var services = new ServiceCollection();
        services.AddZumboPostgreSql(options =>
            options.ConnectionString = "Host=127.0.0.1;Database=unused;Username=unused;Password=unused");
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<
                Zumbo.BuildingBlocks.Application.Persistence.IDocumentRepository<RepositoryContractDocument>>());

        Assert.Contains("No explicit PostgreSQL schema/table mapping", exception.Message, StringComparison.Ordinal);
    }

    private static string FindSourceRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "Backend", "src");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Backend/src from the test output directory.");
    }
}
