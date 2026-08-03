using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Zumbo.BuildingBlocks.Application.Concurrency;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.Audit;

internal static class AuditDiff
{
    private const int OversizedStructuredValueCharacters = 32_768;
    private static readonly Meter Meter = new("Zumbo.Audit", "1.0.0");
    private static readonly Counter<long> ParseFallbacks =
        Meter.CreateCounter<long>("zumbo.audit.diff_parse_fallbacks");
    private static readonly string[] SensitiveFragments =
    [
        "password", "passwd", "token", "secret", "credential", "authorization",
        "cookie", "mfa", "totp", "apikey", "api_key", "signingkey", "privatekey"
    ];

    internal sealed record Result(string? OldValue, string? NewValue, List<AuditChangeDocument> Changes);

    internal static Result Create(string? oldValue, string? newValue)
    {
        var oldFields = Parse(oldValue, "old");
        var newFields = Parse(newValue, "new");
        var fields = oldFields.Keys.Union(newFields.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal);
        var changes = fields.Select(field =>
        {
            oldFields.TryGetValue(field, out var oldFieldValue);
            newFields.TryGetValue(field, out var newFieldValue);
            var redacted = IsSensitive(field, oldFieldValue) || IsSensitive(field, newFieldValue);
            return new AuditChangeDocument
            {
                Field = field,
                OldValue = redacted && oldFieldValue is not null ? "[REDACTED]" : Bound(oldFieldValue),
                NewValue = redacted && newFieldValue is not null ? "[REDACTED]" : Bound(newFieldValue),
                Redacted = redacted
            };
        }).Where(x => x.Redacted || x.OldValue != x.NewValue).ToList();
        return new Result(Summarize(changes, old: true), Summarize(changes, old: false), changes);
    }

    private static Dictionary<string, string?> Parse(string? value, string side)
    {
        if (value is null) return new(StringComparer.Ordinal);
        var oversized = value.Length > OversizedStructuredValueCharacters;
        var looksStructured = LooksStructured(value);
        if (oversized) RecordParseFallback("oversized", side, value);
        try
        {
            using var json = JsonDocument.Parse(value);
            if (json.RootElement.ValueKind == JsonValueKind.Object)
                return json.RootElement.EnumerateObject().ToDictionary(
                    x => x.Name,
                    x => x.Value.ValueKind == JsonValueKind.String ? x.Value.GetString() : x.Value.GetRawText(),
                    StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            if (!oversized && looksStructured)
                RecordParseFallback("malformed_json", side, value);
        }
        return new(StringComparer.Ordinal) { ["value"] = value };
    }

    private static bool LooksStructured(string value)
    {
        var trimmed = value.AsSpan().TrimStart();
        return !trimmed.IsEmpty && trimmed[0] is '{' or '[';
    }

    private static void RecordParseFallback(string reason, string side, string value)
    {
        var size = value.Length switch
        {
            <= 4_096 => "small",
            <= OversizedStructuredValueCharacters => "medium",
            _ => "large"
        };
        ParseFallbacks.Add(
            1,
            new KeyValuePair<string, object?>("reason", reason),
            new KeyValuePair<string, object?>("side", side),
            new KeyValuePair<string, object?>("sensitive", IsSensitive("value", value)),
            new KeyValuePair<string, object?>("size", size));
    }

    private static string? Summarize(IReadOnlyList<AuditChangeDocument> changes, bool old)
    {
        var values = changes.Where(x => (old ? x.OldValue : x.NewValue) is not null)
            .ToDictionary(x => x.Field, x => old ? x.OldValue : x.NewValue, StringComparer.Ordinal);
        if (values.Count == 0) return null;
        if (values.Count == 1 && values.ContainsKey("value")) return values["value"];
        return JsonSerializer.Serialize(values);
    }

    private static bool IsSensitive(string field, string? value) =>
        SensitiveFragments.Any(fragment => field.Contains(fragment, StringComparison.OrdinalIgnoreCase))
        || (value is not null && SensitiveFragments.Any(fragment =>
            value.Contains(fragment + "=", StringComparison.OrdinalIgnoreCase)
            || value.Contains($"\"{fragment}\"", StringComparison.OrdinalIgnoreCase)));

    private static string? Bound(string? value) => value?.Length > 4_000 ? value[..4_000] : value;
}
