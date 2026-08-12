using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Zumbo.Api.Presentation.Binding;

public sealed class MinimalApiRequiredQueryStringModelBinder : IModelBinder
{
    private const string MissingValue = "__zumbo_missing_required_query__";

    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        ArgumentNullException.ThrowIfNull(bindingContext);

        var value = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (value == ValueProviderResult.None)
        {
            bindingContext.Result = ModelBindingResult.Success(MissingValue);
            return Task.CompletedTask;
        }

        bindingContext.ModelState.SetModelValue(bindingContext.ModelName, value);
        bindingContext.Result = ModelBindingResult.Success(value.FirstValue ?? string.Empty);
        return Task.CompletedTask;
    }
}
