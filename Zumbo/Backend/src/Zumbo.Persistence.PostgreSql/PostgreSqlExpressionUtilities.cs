using System.Collections;
using System.Linq.Expressions;
using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Persistence.PostgreSql;

internal static class PostgreSqlExpressionUtilities
{
    internal static Expression StripConvert(Expression expression)
    {
        while (expression is UnaryExpression
               {
                   NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked or ExpressionType.Quote
               } unary)
        {
            expression = unary.Operand;
        }

        return expression;
    }

    internal static object? Evaluate(Expression expression)
    {
        if (DependsOnParameter(expression))
        {
            throw new DocumentQueryException("A document-dependent expression cannot be evaluated as a SQL parameter.");
        }

        try
        {
            return Expression.Lambda<Func<object?>>(Expression.Convert(expression, typeof(object))).Compile().Invoke();
        }
        catch (Exception exception) when (exception is not DocumentQueryException)
        {
            throw new DocumentQueryException($"Expression value could not be evaluated: {expression}.", exception);
        }
    }

    internal static bool DependsOnParameter(Expression expression)
    {
        var visitor = new PostgreSqlParameterFindingVisitor();
        visitor.Visit(expression);
        return visitor.Found;
    }

    internal static IReadOnlyList<string> GetMemberPath(MemberExpression member, ParameterExpression root)
    {
        var path = new Stack<string>();
        Expression? current = member;
        while (current is MemberExpression currentMember)
        {
            if (currentMember.Member.Name == nameof(Nullable<int>.Value)
                && currentMember.Expression is not null
                && Nullable.GetUnderlyingType(currentMember.Expression.Type) is not null)
            {
                current = currentMember.Expression;
                continue;
            }

            path.Push(PostgreSqlJsonTranslation.JsonName(currentMember.Member));
            current = currentMember.Expression;
        }

        if (current != root || path.Count == 0)
        {
            throw new DocumentQueryException("Only document member access can be translated to PostgreSQL.");
        }

        return path.ToList();
    }

    internal static LambdaExpression UnwrapLambda(Expression expression)
    {
        expression = StripConvert(expression);
        if (expression is LambdaExpression lambda)
        {
            return lambda;
        }

        if (expression is UnaryExpression { Operand: LambdaExpression quoted })
        {
            return quoted;
        }

        throw new DocumentQueryException("The collection predicate is not a lambda expression.");
    }

    internal static DocumentQueryException Unsupported(Expression expression) =>
        new($"Expression '{expression}' ({expression.NodeType}) is not supported by PostgreSQL persistence.");

    internal static bool IsComparison(ExpressionType type) =>
        type is ExpressionType.Equal or ExpressionType.NotEqual
            or ExpressionType.GreaterThan or ExpressionType.GreaterThanOrEqual
            or ExpressionType.LessThan or ExpressionType.LessThanOrEqual;

    internal static bool IsContains(MethodCallExpression call) =>
        call.Method.Name == nameof(Enumerable.Contains)
        || (call.Method.Name == nameof(IList.Contains) && call.Object?.Type != typeof(string));

    internal static bool IsAny(MethodCallExpression call) =>
        call.Method.Name == nameof(Enumerable.Any)
        && call.Method.DeclaringType == typeof(Enumerable)
        && call.Arguments.Count is 1 or 2;

    internal static bool IsNull(Expression expression) =>
        !DependsOnParameter(expression) && Evaluate(expression) is null;

    internal static Type NonNullable(Type type) => Nullable.GetUnderlyingType(type) ?? type;
}
