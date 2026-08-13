using Microsoft.AspNetCore.Antiforgery;

namespace Zumbo.Api.Presentation.Binding;

[AttributeUsage(AttributeTargets.Method)]
public sealed class DisableAntiforgeryForControllerAttribute : Attribute, IAntiforgeryMetadata
{
    public bool RequiresValidation => false;
}
