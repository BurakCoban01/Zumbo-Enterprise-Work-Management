using System.Text.Json;
using Zumbo.Modules.Identity;

namespace Zumbo.Api.Infrastructure.Adapters.Platform.PlatformCore.PrivacyDataProcessorAdapter;

internal static class PrivacyStreamSerialization
{
    internal static Task WriteReferenceAsync(
        StreamWriter writer,
        string category,
        PrivacyDataReference reference,
        JsonSerializerOptions streamJson,
        CancellationToken ct) =>
        WriteLineAsync(writer, new
        {
            Kind = "reference",
            Category = category,
            ResourceId = reference.ResourceId,
            Detail = reference.Detail,
            Profile = (UserProfileResponse?)null
        }, streamJson, ct);

    internal static Task WriteLineAsync(
        StreamWriter writer,
        object line,
        JsonSerializerOptions streamJson,
        CancellationToken ct) =>
        writer.WriteLineAsync(JsonSerializer.Serialize(line, streamJson).AsMemory(), ct);
}
