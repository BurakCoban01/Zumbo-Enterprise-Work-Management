using System.Text.RegularExpressions;
using Zumbo.BuildingBlocks.Application.Security;

namespace Zumbo.ArchitectureTests;

public sealed partial class PermissionArchitectureTests
{
    [Fact]
    public void PermissionRoleMaps_ExistOnlyInCentralCatalog()
    {
        var sourceFiles = ProductionSourceFiles();
        var duplicates = sourceFiles
            .Where(path => !path.EndsWith("PermissionCatalog.cs", StringComparison.Ordinal))
            .Where(path => DuplicateRoleMapRegex().IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(BackendRoot(), path))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void PermissionChecks_UseCataloguedPermissionNames()
    {
        var unknown = new List<string>();
        foreach (var path in ProductionSourceFiles())
        {
            var source = File.ReadAllText(path);
            foreach (Match match in PermissionCallRegex().Matches(source))
            {
                var permission = match.Groups[1].Value;
                if (!PermissionCatalog.IsKnownAssignablePermission(permission))
                {
                    unknown.Add($"{Path.GetRelativePath(BackendRoot(), path)}:{permission}");
                }
            }
        }

        Assert.Empty(unknown.OrderBy(x => x, StringComparer.Ordinal));
    }

    private static IReadOnlyList<string> ProductionSourceFiles() =>
        Directory.EnumerateFiles(Path.Combine(BackendRoot(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();

    private static string BackendRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Zumbo.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Backend root could not be located.");
    }

    [GeneratedRegex(@"\b(?:RolePermissions|SystemPermissions|SystemRoles)\s*=", RegexOptions.CultureInvariant)]
    private static partial Regex DuplicateRoleMapRegex();

    [GeneratedRegex(@"(?:EnsurePermissionAsync|HasPermissionAsync)\([^;\r\n]*?""([^""]+)""", RegexOptions.CultureInvariant)]
    private static partial Regex PermissionCallRegex();
}
