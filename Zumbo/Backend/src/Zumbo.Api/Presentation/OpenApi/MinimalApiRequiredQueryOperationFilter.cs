using System.Reflection;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using Zumbo.Api.Presentation.Binding;

namespace Zumbo.Api.Presentation.OpenApi;

public sealed class MinimalApiRequiredQueryOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var requiredNames = context.MethodInfo
            .GetParameters()
            .Where(parameter => parameter.GetCustomAttribute<MinimalApiRequiredQueryAttribute>() is not null)
            .Select(parameter => parameter.Name)
            .Where(name => name is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var parameter in operation.Parameters.Where(parameter =>
                     parameter.In == ParameterLocation.Query && requiredNames.Contains(parameter.Name)))
        {
            parameter.Required = true;
        }
    }
}
