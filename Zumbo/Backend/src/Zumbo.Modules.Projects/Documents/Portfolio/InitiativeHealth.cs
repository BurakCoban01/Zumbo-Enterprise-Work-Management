using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Modules.Projects;

public static class InitiativeHealth
{
    public const string NoUpdate = "NoUpdate";
    public const string OnTrack = "OnTrack";
    public const string AtRisk = "AtRisk";
    public const string OffTrack = "OffTrack";

    public static IReadOnlySet<string> Allowed { get; } = new HashSet<string>(
        [NoUpdate, OnTrack, AtRisk, OffTrack],
        StringComparer.Ordinal);
}
