using System.Diagnostics;
using Microsoft.Extensions.Logging.Console;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

internal sealed class ObservabilityOptions
{
    public bool OtlpEnabled { get; init; }
    public string OtlpEndpoint { get; init; } = "http://127.0.0.1:4317";
    public string ServiceName { get; init; } = "Zumbo.Api";
    public double TraceSampleRatio { get; init; } = 1;
    public int ExportTimeoutMilliseconds { get; init; } = 5_000;
    public int MaxExportBatchSize { get; init; } = 512;
    public int MaxExportQueueSize { get; init; } = 2_048;

    public Uri Validate()
    {
        if (string.IsNullOrWhiteSpace(ServiceName) || ServiceName.Length > 128)
            throw new InvalidOperationException("Observability service name must contain 1 to 128 characters.");
        if (TraceSampleRatio is < 0 or > 1)
            throw new InvalidOperationException("Observability trace sample ratio must be between 0 and 1.");
        if (ExportTimeoutMilliseconds is < 100 or > 60_000)
            throw new InvalidOperationException("Observability export timeout must be between 100 and 60000 milliseconds.");
        if (MaxExportBatchSize is < 1 or > 5_000 || MaxExportQueueSize < MaxExportBatchSize || MaxExportQueueSize > 100_000)
            throw new InvalidOperationException("Observability export queue and batch limits are invalid.");
        if (!Uri.TryCreate(OtlpEndpoint, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("Observability OTLP endpoint must be an absolute HTTP or HTTPS URL.");
        return endpoint;
    }
}

internal static class ObservabilityRegistration
{
    internal static WebApplicationBuilder AddZumboObservability(this WebApplicationBuilder builder)
    {
        var options = builder.Configuration.GetSection("Observability").Get<ObservabilityOptions>()
            ?? new ObservabilityOptions();
        var endpoint = options.Validate();
        builder.Services.Configure<ObservabilityOptions>(builder.Configuration.GetSection("Observability"));
        builder.Logging.Configure(logging => logging.ActivityTrackingOptions =
            ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId | ActivityTrackingOptions.ParentId);

        var resource = ResourceBuilder.CreateDefault()
            .AddService(options.ServiceName, serviceInstanceId: builder.Configuration["Runtime:InstanceId"] ?? Environment.MachineName);
        var telemetry = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resourceBuilder => resourceBuilder.AddService(
                options.ServiceName,
                serviceInstanceId: builder.Configuration["Runtime:InstanceId"] ?? Environment.MachineName))
            .WithTracing(tracing =>
            {
                tracing
                    .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(options.TraceSampleRatio)))
                    .AddAspNetCoreInstrumentation(instrumentation =>
                    {
                        instrumentation.Filter = context => !context.Request.Path.StartsWithSegments("/health/live");
                        instrumentation.EnrichWithHttpRequest = (activity, request) =>
                            activity.SetTag("zumbo.correlation_id", request.HttpContext.TraceIdentifier);
                    })
                    .AddHttpClientInstrumentation()
                    .AddSource("Npgsql", "Zumbo.ExternalDependencies", "Zumbo.DurableMessaging", "Zumbo.Realtime");
                if (options.OtlpEnabled)
                    tracing.AddOtlpExporter(exporter => ConfigureExporter(exporter, endpoint, options));
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter(
                        "Zumbo.ExternalDependencies",
                        "Zumbo.DurableMessaging",
                        "Zumbo.Realtime",
                        "Zumbo.Audit");
                if (options.OtlpEnabled)
                    metrics.AddOtlpExporter(exporter => ConfigureExporter(exporter, endpoint, options));
            });
        _ = telemetry;

        if (options.OtlpEnabled)
        {
            builder.Logging.AddOpenTelemetry(logging =>
            {
                logging.SetResourceBuilder(resource);
                logging.IncludeFormattedMessage = true;
                logging.IncludeScopes = true;
                logging.AddOtlpExporter(exporter => ConfigureExporter(exporter, endpoint, options));
            });
        }
        return builder;
    }

    private static void ConfigureExporter(
        OpenTelemetry.Exporter.OtlpExporterOptions exporter,
        Uri endpoint,
        ObservabilityOptions options)
    {
        exporter.Endpoint = endpoint;
        exporter.ExportProcessorType = OpenTelemetry.ExportProcessorType.Batch;
        exporter.BatchExportProcessorOptions = new OpenTelemetry.BatchExportProcessorOptions<System.Diagnostics.Activity>
        {
            ExporterTimeoutMilliseconds = options.ExportTimeoutMilliseconds,
            MaxExportBatchSize = options.MaxExportBatchSize,
            MaxQueueSize = options.MaxExportQueueSize
        };
    }
}
