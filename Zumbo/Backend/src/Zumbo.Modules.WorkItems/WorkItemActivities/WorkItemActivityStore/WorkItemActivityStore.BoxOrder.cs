using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Zumbo.BuildingBlocks.Application.Persistence;
using Zumbo.BuildingBlocks.Application.Security;
using Zumbo.SharedKernel;

namespace Zumbo.Modules.WorkItems;

public sealed partial class WorkItemActivityStore{

    private static Expression<Func<TDocument, object>> BoxOrder<TDocument, TOrder>(
        Expression<Func<TDocument, TOrder>> expression)
    {
        var body = expression.Body.Type == typeof(object)
            ? expression.Body
            : Expression.Convert(expression.Body, typeof(object));
        return Expression.Lambda<Func<TDocument, object>>(body, expression.Parameters);
    }
}
