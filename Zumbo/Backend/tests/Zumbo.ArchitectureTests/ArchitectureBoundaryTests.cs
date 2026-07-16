using System.Linq.Expressions;
using System.Reflection;
using Zumbo.BuildingBlocks.Infrastructure.Persistence;
using Zumbo.Modules.Identity;
using Zumbo.Modules.WorkItems;
using Zumbo.SharedKernel;

namespace Zumbo.ArchitectureTests;

public sealed class ArchitectureBoundaryTests
{
    [Fact]
    public void DomainAndApplicationTypes_DoNotUseMongoAttributes()
    {
        var assemblies = new[]
        {
            typeof(UserDocument).Assembly,
            typeof(WorkItemDocument).Assembly,
            typeof(Entity).Assembly
        };

        var offendingTypes = assemblies
            .SelectMany(x => x.GetTypes())
            .Where(x => x.GetCustomAttributesData()
                .Any(attribute => attribute.AttributeType.Namespace?.StartsWith("MongoDB.Bson", StringComparison.Ordinal) == true))
            .Select(x => x.FullName)
            .ToList();

        Assert.Empty(offendingTypes);
    }

    [Fact]
    public void DocumentRepository_ExposesExpressionBasedCrudWithoutMongoDriverTypes()
    {
        var interfaceType = typeof(IDocumentRepository<>);
        var methods = interfaceType.GetMethods().Select(x => x.Name).ToArray();

        Assert.Contains("CreateAsync", methods);
        Assert.Contains("SelectAsync", methods);
        Assert.Contains("ListByFilterAsync", methods);
        Assert.Contains("DeleteByFilterAsync", methods);
        Assert.Contains("ReplaceByFilterAsync", methods);
        Assert.Contains("UpdateOneFieldByFilterAsync", methods);

        var publicSignatureTypes = interfaceType
            .GetMethods()
            .SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType))
            .ToList();

        Assert.Contains(publicSignatureTypes, type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Expression<>));
        Assert.DoesNotContain(publicSignatureTypes, type => type.Namespace?.StartsWith("MongoDB.Driver", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void ModuleSpecificAppsettings_FilesExist()
    {
        var apiDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Zumbo.Api"));

        Assert.True(File.Exists(Path.Combine(apiDir, "appsettings.Identity.json")));
        Assert.True(File.Exists(Path.Combine(apiDir, "appsettings.Boards.json")));
        Assert.True(File.Exists(Path.Combine(apiDir, "appsettings.WorkItems.json")));
    }
}

