using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Zumbo.Api.Presentation.Binding;

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class MinimalApiRequiredQueryAttribute : Attribute,
    IBindingSourceMetadata,
    IBinderTypeProviderMetadata,
    IModelNameProvider
{
    public BindingSource BindingSource => BindingSource.Query;

    public Type BinderType => typeof(MinimalApiRequiredQueryStringModelBinder);

    public string? Name => null;
}
