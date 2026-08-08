using Zumbo.Modules.WorkItems;
using Zumbo.Modules.WorkItems.Application.Features.Intake;

namespace Zumbo.Api.Composition.Modules.WorkItems;

internal static class WorkItemIntakeComposition
{
    internal static IServiceCollection AddWorkItemIntakeServices(this IServiceCollection services)
    {
        services.AddScoped<IIntakeWorkItemCreator>(provider =>
            new CreateIntakeWorkItemHandler(provider.GetRequiredService<CreateWorkItemHandler>()));
        services.AddScoped<IIntakeRoutePolicy, IntakeRoutePolicyAdapter>();
        services.AddOptions<IntakeOptions>()
            .BindConfiguration("Intake")
            .Validate(
                options => options.MaxFields is >= 1 and <= 100
                    && options.MaxValues is >= 1 and <= 100
                    && options.MaxAttachments is >= 0 and <= 20
                    && options.MaxAttachmentBytes is >= 1_024 and <= 25 * 1024 * 1024
                    && options.MaxTotalAttachmentBytes >= options.MaxAttachmentBytes
                    && options.MaxTotalAttachmentBytes <= 25 * 1024 * 1024
                    && options.MaxValueCharacters is >= 100 and <= 20_000
                    && options.MaxTotalValueCharacters >= options.MaxValueCharacters
                    && options.MaxTotalValueCharacters <= 100_000,
                "Intake limits are outside the supported bounds.")
            .ValidateOnStart();
        services.AddScoped<IntakeFormService>();
        services.AddScoped<IntakeSubmissionService>();
        return services;
    }
}
