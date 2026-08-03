using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using ApplicationPersistence = Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.BuildingBlocks.Infrastructure.Persistence;

internal static class DocumentFieldSelector
{
    public static PropertyInfo DirectWritableProperty<TDocument, TField>(
        Expression<Func<TDocument, TField>> field)
    {
        Expression body = field.Body;
        if (body is UnaryExpression { NodeType: ExpressionType.Convert } conversion)
        {
            body = conversion.Operand;
        }

        if (body is not MemberExpression
            {
                Expression: ParameterExpression,
                Member: PropertyInfo { CanWrite: true } property
            })
        {
            throw new ApplicationPersistence.DocumentQueryException(
                "Only direct writable document properties can be updated.");
        }

        return property;
    }
}
