using System.Linq.Expressions;

namespace Zumbo.Persistence.PostgreSql;

internal sealed class PostgreSqlParameterFindingVisitor : ExpressionVisitor
{
    public bool Found { get; private set; }

    protected override Expression VisitParameter(ParameterExpression node)
    {
        Found = true;
        return node;
    }
}
