using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public static class DevelopmentWebhookSecurity
{
    public static bool Verify(
        string provider,
        string secret,
        DevelopmentWebhookRequest request,
        DateTimeOffset now)
    {
        if (provider == DevelopmentProviders.GitHub)
        {
            var expectedGitHubSignature =
                "sha256=" + HexHmac(Encoding.UTF8.GetBytes(secret), request.Payload);
            return FixedTimeEquals(expectedGitHubSignature, request.Signature.Trim());
        }

        if (provider != DevelopmentProviders.GitLab
            || string.IsNullOrWhiteSpace(request.Timestamp)
            || !long.TryParse(
                request.Timestamp,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var timestamp))
        {
            return false;
        }

        var occurredAt = DateTimeOffset.FromUnixTimeSeconds(timestamp);
        if ((now - occurredAt).Duration() > TimeSpan.FromSeconds(
            DevelopmentIntegrationLimits.ReplayWindowSeconds))
        {
            return false;
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(secret.StartsWith("whsec_", StringComparison.Ordinal)
                ? secret[6..]
                : string.Empty);
        }
        catch (FormatException)
        {
            return false;
        }

        var prefix = Encoding.UTF8.GetBytes($"{request.DeliveryId}.{request.Timestamp}.");
        var message = new byte[prefix.Length + request.Payload.Length];
        Buffer.BlockCopy(prefix, 0, message, 0, prefix.Length);
        Buffer.BlockCopy(request.Payload, 0, message, prefix.Length, request.Payload.Length);
        using var hmac = new HMACSHA256(key);
        var expectedGitLabSignature =
            "v1," + Convert.ToBase64String(hmac.ComputeHash(message));
        return request.Signature
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(candidate => FixedTimeEquals(expectedGitLabSignature, candidate));
    }

    public static NormalizedDevelopmentEvent? Normalize(
        string provider,
        string eventName,
        byte[] payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 32
            });
            return provider == DevelopmentProviders.GitHub
                ? NormalizeGitHub(eventName, document.RootElement)
                : NormalizeGitLab(eventName, document.RootElement);
        }
        catch (JsonException)
        {
            throw new ValidationException("Development webhook payload is not valid JSON.");
        }
    }

    private static NormalizedDevelopmentEvent? NormalizeGitHub(
        string eventName,
        JsonElement root)
    {
        var repositoryId = Text(root, "repository", "id");
        if (string.IsNullOrWhiteSpace(repositoryId)) return null;
        var normalizedEvent = eventName.Trim().ToLowerInvariant();
        if (normalizedEvent == "pull_request")
        {
            var number = Text(root, "number");
            var title = Text(root, "pull_request", "title");
            var body = Text(root, "pull_request", "body");
            var branch = Text(root, "pull_request", "head", "ref");
            var sha = Text(root, "pull_request", "head", "sha");
            var url = Text(root, "pull_request", "html_url");
            var state = Text(root, "pull_request", "state");
            var merged = Boolean(root, "pull_request", "merged");
            return Event(
                repositoryId,
                DevelopmentLinkKinds.PullRequest,
                "pr:" + number,
                title,
                url,
                branch,
                sha,
                merged ? "Merged" : CanonicalState(state),
                ParseDate(Text(root, "pull_request", "updated_at")),
                title, body, branch);
        }

        if (normalizedEvent == "push")
        {
            var sha = Text(root, "after");
            var reference = Text(root, "ref");
            var branch = reference.StartsWith("refs/heads/", StringComparison.Ordinal)
                ? reference[11..]
                : reference;
            var title = Text(root, "head_commit", "message");
            var url = Text(root, "head_commit", "url");
            return Event(
                repositoryId,
                DevelopmentLinkKinds.Commit,
                "commit:" + sha,
                title,
                url,
                branch,
                sha,
                "Pushed",
                ParseDate(Text(root, "head_commit", "timestamp")),
                title, branch);
        }

        if (normalizedEvent is "status" or "check_run")
        {
            var sha = normalizedEvent == "status"
                ? Text(root, "sha")
                : Text(root, "check_run", "head_sha");
            var context = normalizedEvent == "status"
                ? Text(root, "context")
                : Text(root, "check_run", "name");
            var state = normalizedEvent == "status"
                ? Text(root, "state")
                : Text(root, "check_run", "conclusion");
            if (string.IsNullOrWhiteSpace(state))
                state = Text(root, "check_run", "status");
            var url = normalizedEvent == "status"
                ? Text(root, "target_url")
                : Text(root, "check_run", "html_url");
            var occurredAt = normalizedEvent == "status"
                ? ParseDate(Text(root, "updated_at"))
                : ParseDate(Text(root, "check_run", "completed_at"))
                    ?? ParseDate(Text(root, "check_run", "started_at"));
            return Event(
                repositoryId,
                DevelopmentLinkKinds.Build,
                $"build:{sha}:{context}",
                context,
                url,
                null,
                sha,
                CanonicalState(state),
                occurredAt,
                context);
        }

        return null;
    }

    private static NormalizedDevelopmentEvent? NormalizeGitLab(
        string eventName,
        JsonElement root)
    {
        var repositoryId = Text(root, "project", "id");
        if (string.IsNullOrWhiteSpace(repositoryId)) return null;
        var normalizedEvent = eventName.Trim().ToLowerInvariant();
        if (normalizedEvent.Contains("merge request", StringComparison.Ordinal)
            || Text(root, "object_kind") == "merge_request")
        {
            var iid = Text(root, "object_attributes", "iid");
            var title = Text(root, "object_attributes", "title");
            var description = Text(root, "object_attributes", "description");
            var branch = Text(root, "object_attributes", "source_branch");
            var sha = Text(root, "object_attributes", "last_commit", "id");
            var url = Text(root, "object_attributes", "url");
            var state = Text(root, "object_attributes", "state");
            return Event(
                repositoryId,
                DevelopmentLinkKinds.PullRequest,
                "mr:" + iid,
                title,
                url,
                branch,
                sha,
                CanonicalState(state),
                ParseDate(Text(root, "object_attributes", "updated_at")),
                title, description, branch);
        }

        if (normalizedEvent.Contains("push", StringComparison.Ordinal)
            || Text(root, "object_kind") == "push")
        {
            var sha = Text(root, "after");
            var reference = Text(root, "ref");
            var branch = reference.StartsWith("refs/heads/", StringComparison.Ordinal)
                ? reference[11..]
                : reference;
            var title = FirstArrayText(root, "commits", "message");
            var url = FirstArrayText(root, "commits", "url");
            return Event(
                repositoryId,
                DevelopmentLinkKinds.Commit,
                "commit:" + sha,
                title,
                url,
                branch,
                sha,
                "Pushed",
                ParseDate(FirstArrayText(root, "commits", "timestamp")),
                title, branch);
        }

        if (normalizedEvent.Contains("pipeline", StringComparison.Ordinal)
            || Text(root, "object_kind") == "pipeline")
        {
            var id = Text(root, "object_attributes", "id");
            var sha = Text(root, "object_attributes", "sha");
            var status = Text(root, "object_attributes", "status");
            var url = Text(root, "object_attributes", "url");
            return Event(
                repositoryId,
                DevelopmentLinkKinds.Build,
                "build:" + id,
                "Pipeline " + id,
                url,
                Text(root, "object_attributes", "ref"),
                sha,
                CanonicalState(status),
                ParseDate(Text(root, "object_attributes", "updated_at")),
                Text(root, "object_attributes", "ref"));
        }

        return null;
    }

    private static NormalizedDevelopmentEvent Event(
        string repositoryId,
        string kind,
        string externalId,
        string title,
        string url,
        string? branch,
        string? sha,
        string status,
        DateTimeOffset? occurredAt,
        params string?[] references)
    {
        var referenceTexts = references
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .ToList();
        _ = DevelopmentWebhookReferencePolicy.ExtractWithinLimit(referenceTexts);
        return new(
            Required(repositoryId, 200),
            kind,
            Required(externalId, 300),
            Optional(title, 200) ?? kind,
            Required(url, 2_048),
            Optional(branch, 255),
            Optional(sha, 128),
            status,
            occurredAt,
            referenceTexts);
    }

    private static string CanonicalState(string value) => value.Trim().ToLowerInvariant() switch
    {
        "open" or "opened" or "reopened" => "Open",
        "merged" => "Merged",
        "closed" => "Closed",
        "success" or "successful" or "completed" => "Success",
        "failure" or "failed" or "error" or "cancelled" or "canceled" => "Failed",
        "pending" or "queued" => "Pending",
        "running" or "in_progress" => "Running",
        _ => "Unknown"
    };

    private static DateTimeOffset? ParseDate(string value) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed
            : null;

    private static string Text(JsonElement root, params string[] path)
    {
        var current = root;
        foreach (var part in path)
        {
            if (current.ValueKind != JsonValueKind.Object
                || !current.TryGetProperty(part, out current))
            {
                return string.Empty;
            }
        }

        return current.ValueKind switch
        {
            JsonValueKind.String => current.GetString() ?? string.Empty,
            JsonValueKind.Number => current.GetRawText(),
            _ => string.Empty
        };
    }

    private static bool Boolean(JsonElement root, params string[] path)
    {
        var current = root;
        foreach (var part in path)
        {
            if (current.ValueKind != JsonValueKind.Object
                || !current.TryGetProperty(part, out current))
            {
                return false;
            }
        }
        return current.ValueKind == JsonValueKind.True;
    }

    private static string FirstArrayText(
        JsonElement root,
        string arrayProperty,
        string itemProperty)
    {
        if (!root.TryGetProperty(arrayProperty, out var array)
            || array.ValueKind != JsonValueKind.Array
            || array.GetArrayLength() == 0)
        {
            return string.Empty;
        }
        var first = array[0];
        return first.ValueKind == JsonValueKind.Object
            && first.TryGetProperty(itemProperty, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;
    }

    private static string Required(string value, int maximum)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 || normalized.Length > maximum)
            throw new ValidationException("Development webhook field is outside its supported bound.");
        return normalized;
    }

    private static string? Optional(string? value, int maximum)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized)) return null;
        return normalized.Length <= maximum ? normalized : normalized[..maximum];
    }

    private static string HexHmac(byte[] key, byte[] payload)
    {
        using var hmac = new HMACSHA256(key);
        return Convert.ToHexString(hmac.ComputeHash(payload)).ToLowerInvariant();
    }

    private static bool FixedTimeEquals(string expected, string actual)
    {
        var expectedBytes = Encoding.ASCII.GetBytes(expected);
        var actualBytes = Encoding.ASCII.GetBytes(actual);
        return expectedBytes.Length == actualBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }
}
