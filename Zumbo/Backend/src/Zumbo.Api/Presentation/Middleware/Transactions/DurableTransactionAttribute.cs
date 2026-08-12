using Microsoft.AspNetCore.Mvc;

namespace Zumbo.Api.Presentation.Middleware.Transactions;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class DurableTransactionAttribute : TypeFilterAttribute
{
    public DurableTransactionAttribute(string ownerModule, string? bypassPathPrefix = null)
        : base(typeof(DurableTransactionActionFilter))
    {
        if (string.IsNullOrWhiteSpace(ownerModule))
        {
            throw new ArgumentException("A durable transaction owner module is required.", nameof(ownerModule));
        }

        Arguments = [ownerModule, bypassPathPrefix ?? string.Empty];
    }
}
