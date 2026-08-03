using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace Zumbo.ArchitectureTests.RefactorValidation;

internal static class RefactorSourceReader
{
    private static readonly ConcurrentDictionary<string, Lazy<IReadOnlyList<SourceFile>>> Cache =
        new(StringComparer.Ordinal);

    internal static string ReadGitFile(string repositoryDirectory, string gitRef, string projectRelativePath) =>
        RunGit(repositoryDirectory, "show", $"{gitRef}:Zumbo/{projectRelativePath}");

    internal static IReadOnlyList<SourceFile> ReadGit(string repositoryDirectory, string gitRef) =>
        Cache.GetOrAdd(
            $"git|{repositoryDirectory}|{gitRef}",
            _ => new Lazy<IReadOnlyList<SourceFile>>(() => ReadGitCore(repositoryDirectory, gitRef))).Value;

    private static IReadOnlyList<SourceFile> ReadGitCore(string repositoryDirectory, string gitRef) =>
        RunGit(
                repositoryDirectory,
                "ls-tree", "-r", "--name-only", gitRef, "--", "Zumbo/Backend/src")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .Select(path => new SourceFile(
                path["Zumbo/".Length..],
                RunGit(repositoryDirectory, "show", $"{gitRef}:{path}")))
            .ToArray();

    internal static IReadOnlyList<SourceFile> ReadWorkingTree(string projectDirectory)
        => Cache.GetOrAdd(
            $"working|{projectDirectory}",
            _ => new Lazy<IReadOnlyList<SourceFile>>(() => ReadWorkingTreeCore(projectDirectory))).Value;

    private static IReadOnlyList<SourceFile> ReadWorkingTreeCore(string projectDirectory)
    {
        var sourceDirectory = Path.Combine(projectDirectory, "Backend", "src");
        return Directory.GetFiles(sourceDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildArtifact(sourceDirectory, path))
            .Order(StringComparer.Ordinal)
            .Select(path => new SourceFile(
                Path.GetRelativePath(projectDirectory, path).Replace('\\', '/'),
                File.ReadAllText(path)))
            .ToArray();
    }

    private static bool IsBuildArtifact(string sourceDirectory, string path)
    {
        var relativeSegments = Path.GetRelativePath(sourceDirectory, path)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return relativeSegments.Any(segment => segment is "bin" or "obj");
    }

    private static string RunGit(string repositoryDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = repositoryDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start git.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {error}");
        }

        return output.Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    internal sealed record SourceFile(string Path, string Content);
}
