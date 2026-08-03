using System.Collections;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;
using NpgsqlTypes;
using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Persistence.PostgreSql;

internal sealed partial class PostgreSqlExpressionTranslator{

    private static IReadOnlyList<string> GetMemberPath(MemberExpression member, ParameterExpression root)
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

            path.Push(JsonName(currentMember.Member));
            current = currentMember.Expression;
        }

        if (current != root || path.Count == 0)
        {
            throw new DocumentQueryException("Only document member access can be translated to PostgreSQL.");
        }

        return path.ToList();
    }
}
