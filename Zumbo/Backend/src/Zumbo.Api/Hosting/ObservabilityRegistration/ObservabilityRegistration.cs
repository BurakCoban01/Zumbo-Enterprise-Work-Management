using System.Diagnostics;
using Microsoft.Extensions.Logging.Console;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

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
