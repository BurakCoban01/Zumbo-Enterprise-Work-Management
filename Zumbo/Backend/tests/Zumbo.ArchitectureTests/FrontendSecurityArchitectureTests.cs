using System.Text.Json;
using System.Text.RegularExpressions;
using Zumbo.BuildingBlocks.Application.Security;

namespace Zumbo.ArchitectureTests;

public sealed class FrontendSecurityArchitectureTests
{
    private static readonly string[] ForbiddenDomSinks =
    [
        ".innerHTML",
        ".outerHTML",
        "insertAdjacentHTML(",
        "document.write(",
        "$sce.trustAsHtml(",
        "ng-bind-html",
        "new Function(",
        "window.eval("
    ];

    private static readonly string[] ForbiddenBrowserCredentialMarkers =
    [
        "setItem('zumbo.accessToken'",
        "setItem(\"zumbo.accessToken\"",
        "setItem('zumbo.refreshToken'",
        "setItem(\"zumbo.refreshToken\"",
        "accessTokenFactory"
    ];

    [Fact]
    public void ProductionFrontends_DoNotUseExecutableHtmlSinks()
    {
        var violations = FrontendSources()
            .SelectMany(path => ForbiddenDomSinks
                .Where(marker => File.ReadAllText(path).Contains(marker, StringComparison.Ordinal))
                .Select(marker => $"{Path.GetRelativePath(RepositoryRoot(), path)}:{marker}"))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void ProductionFrontends_DoNotPersistOrExposeBearerCredentials()
    {
        var violations = FrontendSources()
            .SelectMany(path => ForbiddenBrowserCredentialMarkers
                .Where(marker => File.ReadAllText(path).Contains(marker, StringComparison.Ordinal))
                .Select(marker => $"{Path.GetRelativePath(RepositoryRoot(), path)}:{marker}"))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void ProductionDefaults_DoNotContainUsableSecretsOrWildcardHosts()
    {
        Assert.Empty(new JwtOptions().SigningKey);

        foreach (var relativePath in new[]
                 {
                     "Backend/src/Zumbo.Api/appsettings.json",
                     "Backend/src/Zumbo.Gateway/appsettings.json"
                 })
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), relativePath)));
            var allowedHosts = document.RootElement.GetProperty("AllowedHosts").GetString() ?? string.Empty;
            Assert.DoesNotContain(
                allowedHosts.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                host => host == "*");
        }

        using var apiSettings = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(RepositoryRoot(), "Backend/src/Zumbo.Api/appsettings.json")));
        var jwt = apiSettings.RootElement.GetProperty("Jwt");
        Assert.Equal(string.Empty, jwt.GetProperty("SigningKey").GetString());
        Assert.Empty(jwt.GetProperty("SigningKeys").EnumerateObject());
    }

    [Fact]
    public void ProductionFrontendDocuments_HaveNoCdnOrInlineCspBlockers()
    {
        var violations = new List<string>();
        foreach (var path in new[]
                 {
                     Path.Combine(RepositoryRoot(), "Frontend/desktop-bulma/index.html"),
                     Path.Combine(RepositoryRoot(), "Frontend/mobile-ionic/index.html")
                 })
        {
            var body = File.ReadAllText(path);
            var relativePath = Path.GetRelativePath(RepositoryRoot(), path);
            if (Regex.IsMatch(body, "(?:src|href)\\s*=\\s*[\\\"'](?:https?:)?//", RegexOptions.IgnoreCase))
            {
                violations.Add(relativePath + ":cdn");
            }

            if (Regex.IsMatch(body, "<style\\b|\\sstyle\\s*=", RegexOptions.IgnoreCase))
            {
                violations.Add(relativePath + ":inline-style");
            }

            foreach (Match script in Regex.Matches(
                         body,
                         "<script\\b(?<attributes>[^>]*)>(?<body>[\\s\\S]*?)</script>",
                         RegexOptions.IgnoreCase))
            {
                var attributes = script.Groups["attributes"].Value;
                var isExternal = Regex.IsMatch(attributes, "\\bsrc\\s*=", RegexOptions.IgnoreCase);
                var isAngularTemplate = Regex.IsMatch(
                    attributes,
                    "\\btype\\s*=\\s*[\\\"']text/ng-template[\\\"']",
                    RegexOptions.IgnoreCase);
                if (!isExternal && !isAngularTemplate && !string.IsNullOrWhiteSpace(script.Groups["body"].Value))
                {
                    violations.Add(relativePath + ":inline-script");
                }
            }
        }

        Assert.Empty(violations);
    }

    private static IReadOnlyList<string> FrontendSources() =>
        Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "Frontend"), "*.js", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}vendor{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}dist{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "Frontend")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Repository root could not be located.");
    }
}
