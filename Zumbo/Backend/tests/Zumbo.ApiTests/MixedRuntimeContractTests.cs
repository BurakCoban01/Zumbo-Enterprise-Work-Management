using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Zumbo.Api.Presentation.Authorization;

namespace Zumbo.ApiTests;

public sealed class MixedRuntimeContractTests(WebApplicationFactory<Program> baseFactory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task ApiRoutesAndOpenApi_MatchApprovedMixedRuntimeContract()
    {
        using var factory = baseFactory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = factory.CreateClient();

        var operations = factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => NormalizeRoute(endpoint).StartsWith("/api/", StringComparison.Ordinal))
            .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
                .Select(method => (Method: method.ToUpperInvariant(), Route: NormalizeRoute(endpoint), Endpoint: endpoint)))
            .ToList();

        var duplicates = operations
            .GroupBy(operation => $"{operation.Method}|{operation.Route}", StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.Empty(duplicates);

        using var swagger = await client.GetFromJsonAsync<JsonDocument>("/swagger/v1/swagger.json");
        Assert.NotNull(swagger);
        var paths = swagger.RootElement.GetProperty("paths");
        var lines = new List<string>();

        foreach (var operation in operations.OrderBy(item => item.Route, StringComparer.Ordinal)
                     .ThenBy(item => item.Method, StringComparer.Ordinal))
        {
            Assert.True(paths.TryGetProperty(operation.Route, out var path), $"OpenAPI path missing: {operation.Route}");
            Assert.True(
                path.TryGetProperty(operation.Method.ToLowerInvariant(), out var openApiOperation),
                $"OpenAPI operation missing: {operation.Method} {operation.Route}");
            lines.Add(DescribeRuntime(operation.Method, operation.Route, operation.Endpoint)
                + "|openapi=" + DescribeOpenApiOperation(openApiOperation));
        }

        if (swagger.RootElement.TryGetProperty("components", out var components)
            && components.TryGetProperty("schemas", out var schemas))
        {
            lines.AddRange(schemas.EnumerateObject()
                .OrderBy(schema => schema.Name, StringComparer.Ordinal)
                .Select(schema => $"SCHEMA|{schema.Name}|sha256={Hash(schema.Value)}"));
        }

        var actual = string.Join('\n', lines) + '\n';
        var approvedPath = Path.Combine(BackendRoot(), "tests", "Zumbo.ApiTests", "MixedRuntimeContract.approved.txt");
        if (Environment.GetEnvironmentVariable("ZUMBO_APPROVE_MIXED_RUNTIME_CONTRACT") == "1")
        {
            File.WriteAllText(approvedPath, actual.Replace("\n", Environment.NewLine, StringComparison.Ordinal));
        }

        var expected = File.ReadAllText(approvedPath).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Equal(expected, actual);
    }

    private static string DescribeRuntime(string method, string route, RouteEndpoint endpoint)
    {
        var owner = endpoint.Metadata.GetMetadata<ControllerActionDescriptor>() is { } action
            ? $"controller:{action.ControllerTypeInfo.FullName}.{action.MethodInfo.Name}"
            : "minimal";
        var authorization = endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null
            ? "anonymous"
            : endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Count > 0 ? "required" : "none";
        var permission = endpoint.Metadata.GetOrderedMetadata<IEndpointPermissionMetadata>().LastOrDefault();
        var permissionValue = permission is null
            ? "none"
            : $"{permission.Permission}:{(permission.IsGlobal ? "global" : "resource")}";
        var ratePolicies = endpoint.Metadata.GetOrderedMetadata<EnableRateLimitingAttribute>()
            .Select(attribute => attribute.PolicyName ?? "<instance>")
            .ToList();
        var rate = ratePolicies.Count == 0 ? "none" : ratePolicies[^1];
        var tags = endpoint.Metadata.GetOrderedMetadata<ITagsMetadata>()
            .SelectMany(metadata => metadata.Tags)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);

        return $"{method}|{route}|owner={owner}|auth={authorization}|permission={permissionValue}"
            + $"|rate={rate}|tags={string.Join(',', tags)}";
    }

    private static string DescribeOpenApiOperation(JsonElement operation)
    {
        IEnumerable<string> parameters = operation.TryGetProperty("parameters", out var parameterArray)
            ? parameterArray.EnumerateArray()
                .Select(parameter =>
                {
                    var schema = parameter.TryGetProperty("schema", out var value) ? Hash(value) : "none";
                    return $"{parameter.GetProperty("in").GetString()}:{parameter.GetProperty("name").GetString()}"
                        + $":{(parameter.TryGetProperty("required", out var required) && required.GetBoolean() ? "required" : "optional")}:{schema}";
                })
                .Order(StringComparer.Ordinal)
            : [];
        var requestBody = operation.TryGetProperty("requestBody", out var body)
            ? DescribeContent(body.GetProperty("content"))
            : "none";
        var responses = operation.GetProperty("responses").EnumerateObject()
            .OrderBy(response => response.Name, StringComparer.Ordinal)
            .Select(response =>
            {
                var headers = response.Value.TryGetProperty("headers", out var headerObject)
                    ? string.Join(',', headerObject.EnumerateObject().Select(header => header.Name).Order(StringComparer.Ordinal))
                    : string.Empty;
                var content = response.Value.TryGetProperty("content", out var contentObject)
                    ? DescribeContent(contentObject)
                    : "none";
                return $"{response.Name}[headers={headers};content={content}]";
            });

        return $"params={string.Join(',', parameters)};request={requestBody};responses={string.Join(',', responses)}";
    }

    private static string DescribeContent(JsonElement content) =>
        string.Join(
            ',',
            content.EnumerateObject()
                .OrderBy(item => item.Name, StringComparer.Ordinal)
                .Select(item =>
                {
                    var schema = item.Value.TryGetProperty("schema", out var value) ? Hash(value) : "none";
                    return $"{item.Name}:{schema}";
                }));

    private static string Hash(JsonElement value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Canonicalize(value)))).ToLowerInvariant();

    private static string Canonicalize(JsonElement value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteCanonical(writer, value);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray())
                {
                    WriteCanonical(writer, item);
                }
                writer.WriteEndArray();
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }

    private static string NormalizeRoute(RouteEndpoint endpoint)
    {
        var route = endpoint.RoutePattern.RawText ?? string.Empty;
        var rooted = route.StartsWith("/", StringComparison.Ordinal) ? route : $"/{route}";
        var withoutConstraints = Regex.Replace(rooted, @"\{([^}:]+):[^}]+\}", "{$1}");
        return withoutConstraints.Length > 1 ? withoutConstraints.TrimEnd('/') : withoutConstraints;
    }

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
}
