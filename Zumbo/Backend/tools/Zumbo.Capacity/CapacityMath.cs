using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Zumbo.Capacity;

internal static class CapacityMath
{
    public static double Percentile(IReadOnlyList<double> sorted, double percentile)
    {
        if (sorted.Count == 0)
        {
            return 0;
        }

        var index = (int)Math.Ceiling(percentile * sorted.Count) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
    }

    public static string OperationFor(long sequence) => (sequence % 20) switch
    {
        < 8 => "read",
        < 12 => "search",
        < 16 => "report",
        < 18 => "write",
        18 => "external",
        _ => "upload"
    };

    public static string CreateDeterministicPasswordHash(string password, CapacityProfile profile)
    {
        const int iterations = 210_000;
        var seed = SHA256.HashData(Encoding.UTF8.GetBytes($"zumbo-capacity:{profile.Name}:{profile.RunId}"));
        var salt = seed[..16];
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            32);
        return $"PBKDF2-SHA256${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static ResourceSnapshot ResourceSince(Process process, TimeSpan cpuBefore, long diskFreeBytes) => new(
        Math.Max(0, (process.TotalProcessorTime - cpuBefore).TotalSeconds),
        process.WorkingSet64,
        process.PeakWorkingSet64,
        GC.GetTotalMemory(false),
        diskFreeBytes);

    public static long GetDiskFreeBytes()
    {
        var root = Path.GetPathRoot(AppContext.BaseDirectory)
            ?? throw new InvalidOperationException("Capacity tool disk root could not be resolved.");
        return new DriveInfo(root).AvailableFreeSpace;
    }
}
