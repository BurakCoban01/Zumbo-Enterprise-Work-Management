using System.Collections;
using System.Linq.Expressions;
using System.Text.Json;
using Zumbo.BuildingBlocks.Application.Persistence;

namespace Zumbo.Persistence.PostgreSql;

internal sealed class PostgreSqlPredicateTranslator(
    JsonSerializerOptions jsonOptions,
    PostgreSqlParameterCollector parameterCollector,
    PostgreSqlTranslationState state)
{
    internal string TranslatePredicate<TDocument>(Expression<Func<TDocument, bool>> expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        return VisitPredicate(expression.Body, new PostgreSqlTranslationScope(expression.Parameters[0], "document", HasColumns: true));
    }

    internal string TranslateOrder<TDocument>(Expression<Func<TDocument, object>> expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        return VisitScalar(PostgreSqlExpressionUtilities.StripConvert(expression.Body), new PostgreSqlTranslationScope(expression.Parameters[0], "document", HasColumns: true)).Sql;
    }

    internal string VisitPredicate(Expression expression, PostgreSqlTranslationScope scope)
    {
        expression = PostgreSqlExpressionUtilities.StripConvert(expression);
        if (!PostgreSqlExpressionUtilities.DependsOnParameter(expression))
        {
            return PostgreSqlExpressionUtilities.Evaluate(expression) is true ? "TRUE" : "FALSE";
        }

        return expression switch
        {
            BinaryExpression { NodeType: ExpressionType.AndAlso } binary =>
                $"({VisitPredicate(binary.Left, scope)} AND {VisitPredicate(binary.Right, scope)})",
            BinaryExpression { NodeType: ExpressionType.OrElse } binary =>
                $"({VisitPredicate(binary.Left, scope)} OR {VisitPredicate(binary.Right, scope)})",
            BinaryExpression binary when PostgreSqlExpressionUtilities.IsComparison(binary.NodeType) => VisitComparison(binary, scope),
            UnaryExpression { NodeType: ExpressionType.Not } unary => $"(NOT {VisitPredicate(unary.Operand, scope)})",
            MethodCallExpression call => VisitBooleanMethod(call, scope),
            MemberExpression member when PostgreSqlExpressionUtilities.NonNullable(member.Type) == typeof(bool) =>
                $"({VisitScalar(member, scope).Sql} IS TRUE)",
            ConstantExpression { Value: bool value } => value ? "TRUE" : "FALSE",
            _ => throw PostgreSqlExpressionUtilities.Unsupported(expression)
        };
    }

    internal string VisitComparison(BinaryExpression expression, PostgreSqlTranslationScope scope)
    {
        var leftIsNull = PostgreSqlExpressionUtilities.IsNull(expression.Left);
        var rightIsNull = PostgreSqlExpressionUtilities.IsNull(expression.Right);
        if (leftIsNull || rightIsNull)
        {
            if (leftIsNull && rightIsNull)
            {
                return expression.NodeType == ExpressionType.Equal ? "TRUE" : "FALSE";
            }

            if (expression.NodeType is not (ExpressionType.Equal or ExpressionType.NotEqual))
            {
                return "FALSE";
            }

            var operand = VisitScalar(leftIsNull ? expression.Right : expression.Left, scope).Sql;
            return expression.NodeType == ExpressionType.Equal
                ? $"({operand} IS NULL)"
                : $"({operand} IS NOT NULL)";
        }

        var left = VisitScalar(expression.Left, scope);
        var right = VisitScalar(expression.Right, scope, left.Type);
        var sqlOperator = expression.NodeType switch
        {
            ExpressionType.Equal => "=",
            ExpressionType.NotEqual => "<>",
            ExpressionType.GreaterThan => ">",
            ExpressionType.GreaterThanOrEqual => ">=",
            ExpressionType.LessThan => "<",
            ExpressionType.LessThanOrEqual => "<=",
            _ => throw PostgreSqlExpressionUtilities.Unsupported(expression)
        };
        return $"({left.Sql} {sqlOperator} {right.Sql})";
    }

    internal string VisitBooleanMethod(MethodCallExpression call, PostgreSqlTranslationScope scope)
    {
        if (PostgreSqlExpressionUtilities.IsAny(call))
        {
            return VisitAny(call, scope);
        }

        if (call.Object?.Type == typeof(string))
        {
            var source = VisitScalar(call.Object, scope);
            var argument = VisitScalar(call.Arguments[0], scope, typeof(string));
            return call.Method.Name switch
            {
                nameof(string.Contains) => $"(strpos({source.Sql}, {argument.Sql}) > 0)",
                nameof(string.StartsWith) => $"(strpos({source.Sql}, {argument.Sql}) = 1)",
                nameof(string.EndsWith) =>
                    $"(right({source.Sql}, length({argument.Sql})) = {argument.Sql})",
                nameof(string.Equals) => VisitStringEquals(call, scope),
                _ => throw PostgreSqlExpressionUtilities.Unsupported(call)
            };
        }

        if (PostgreSqlExpressionUtilities.IsContains(call))
        {
            return VisitContains(call, scope);
        }

        if (call.Method.Name == nameof(object.Equals))
        {
            var leftExpression = call.Object ?? call.Arguments[0];
            var rightExpression = call.Object is null ? call.Arguments[1] : call.Arguments[0];
            var left = VisitScalar(leftExpression, scope);
            var right = VisitScalar(rightExpression, scope, left.Type);
            return $"({left.Sql} IS NOT DISTINCT FROM {right.Sql})";
        }

        throw PostgreSqlExpressionUtilities.Unsupported(call);
    }

    internal string VisitStringEquals(MethodCallExpression call, PostgreSqlTranslationScope scope)
    {
        var source = VisitScalar(call.Object!, scope);
        var argument = VisitScalar(call.Arguments[0], scope, typeof(string));
        if (call.Arguments.Count == 1)
        {
            return $"({source.Sql} = {argument.Sql})";
        }

        var comparison = PostgreSqlExpressionUtilities.Evaluate(call.Arguments[1]);
        return comparison switch
        {
            StringComparison.Ordinal => $"({source.Sql} = {argument.Sql})",
            StringComparison.OrdinalIgnoreCase => $"(lower({source.Sql}) = lower({argument.Sql}))",
            _ => throw new DocumentQueryException("Only ordinal string comparison can be translated to PostgreSQL.")
        };
    }

    internal string VisitContains(MethodCallExpression call, PostgreSqlTranslationScope scope)
    {
        Expression sourceExpression;
        Expression itemExpression;
        if (call.Object is not null)
        {
            sourceExpression = call.Object;
            itemExpression = call.Arguments[0];
        }
        else
        {
            sourceExpression = call.Arguments[0];
            itemExpression = call.Arguments[1];
        }

        if (!PostgreSqlExpressionUtilities.DependsOnParameter(sourceExpression))
        {
            if (PostgreSqlExpressionUtilities.Evaluate(sourceExpression) is not IEnumerable values)
            {
                throw PostgreSqlExpressionUtilities.Unsupported(call);
            }

            var item = VisitScalar(itemExpression, scope);
            var entries = values.Cast<object?>().ToList();
            if (entries.Count == 0)
            {
                return "FALSE";
            }

            var valueParameters = entries.Select(value => parameterCollector.AddParameter(value, item.Type));
            return $"({item.Sql} IN ({string.Join(", ", valueParameters)}))";
        }

        var jsonSource = VisitJson(sourceExpression, scope);
        if (PostgreSqlExpressionUtilities.DependsOnParameter(itemExpression))
        {
            throw new DocumentQueryException("A document collection can only be searched for a captured scalar value.");
        }

        var itemValue = PostgreSqlExpressionUtilities.Evaluate(itemExpression);
        var json = JsonSerializer.Serialize(new[] { itemValue }, jsonOptions);
        var parameter = parameterCollector.AddJsonParameter(json);
        return $"(COALESCE({jsonSource}, '[]'::jsonb) @> {parameter}::jsonb)";
    }

    internal string VisitAny(MethodCallExpression call, PostgreSqlTranslationScope scope)
    {
        var sourceExpression = call.Arguments[0];
        var jsonSource = VisitJson(sourceExpression, scope);
        if (call.Arguments.Count == 1)
        {
            return $"(jsonb_array_length(COALESCE({jsonSource}, '[]'::jsonb)) > 0)";
        }

        var lambda = PostgreSqlExpressionUtilities.UnwrapLambda(call.Arguments[1]);
        var alias = $"j{state.JsonAliasIndex++}";
        var predicate = VisitPredicate(lambda.Body, new PostgreSqlTranslationScope(lambda.Parameters[0], $"{alias}.value", HasColumns: false));
        return $"EXISTS (SELECT 1 FROM jsonb_array_elements(COALESCE({jsonSource}, '[]'::jsonb)) AS {alias}(value) WHERE {predicate})";
    }

    internal PostgreSqlSqlValue VisitScalar(Expression expression, PostgreSqlTranslationScope scope, Type? preferredType = null)
    {
        expression = PostgreSqlExpressionUtilities.StripConvert(expression);
        if (!PostgreSqlExpressionUtilities.DependsOnParameter(expression))
        {
            var value = PostgreSqlExpressionUtilities.Evaluate(expression);
            var type = preferredType ?? expression.Type;
            return new PostgreSqlSqlValue(parameterCollector.AddParameter(value, type), PostgreSqlExpressionUtilities.NonNullable(type));
        }

        if (expression == scope.Parameter && !scope.HasColumns)
        {
            return new PostgreSqlSqlValue(PostgreSqlJsonTranslation.CastJsonText($"{scope.JsonExpression} #>> '{{}}'", expression.Type), PostgreSqlExpressionUtilities.NonNullable(expression.Type));
        }

        if (expression is MemberExpression member)
        {
            if (member.Member.Name == nameof(Nullable<int>.Value)
                && Nullable.GetUnderlyingType(member.Expression!.Type) is not null)
            {
                return VisitScalar(member.Expression, scope, Nullable.GetUnderlyingType(member.Expression.Type));
            }

            if (member.Member.Name == nameof(Nullable<int>.HasValue)
                && Nullable.GetUnderlyingType(member.Expression!.Type) is not null)
            {
                var nullable = VisitScalar(member.Expression, scope);
                return new PostgreSqlSqlValue($"({nullable.Sql} IS NOT NULL)", typeof(bool));
            }

            var path = PostgreSqlExpressionUtilities.GetMemberPath(member, scope.Parameter);
            if (scope.HasColumns && path.Count == 1 && path[0] == nameof(IDocument.Id))
            {
                return new PostgreSqlSqlValue("id", typeof(string));
            }

            if (scope.HasColumns && path.Count == 1 && path[0] == nameof(IVersionedDocument.Version))
            {
                return new PostgreSqlSqlValue("version", typeof(long));
            }

            var jsonText = PostgreSqlJsonTranslation.JsonText(scope.JsonExpression, path);
            return new PostgreSqlSqlValue(PostgreSqlJsonTranslation.CastJsonText(jsonText, member.Type), PostgreSqlExpressionUtilities.NonNullable(member.Type));
        }

        if (expression is MethodCallExpression call
            && call.Object is not null
            && call.Arguments.Count == 0
            && call.Method.Name is nameof(string.ToLower) or nameof(string.ToLowerInvariant))
        {
            var operand = VisitScalar(call.Object, scope, typeof(string));
            return new PostgreSqlSqlValue($"lower({operand.Sql})", typeof(string));
        }

        if (expression is MethodCallExpression stringCompare
            && stringCompare.Method.DeclaringType == typeof(string)
            && stringCompare.Method.Name is nameof(string.Compare) or nameof(string.CompareOrdinal)
            && stringCompare.Arguments.Count >= 2)
        {
            var left = VisitScalar(stringCompare.Arguments[0], scope, typeof(string));
            var right = VisitScalar(stringCompare.Arguments[1], scope, typeof(string));
            return new PostgreSqlSqlValue(
                $"(CASE WHEN {left.Sql} < {right.Sql} THEN -1 WHEN {left.Sql} > {right.Sql} THEN 1 ELSE 0 END)",
                typeof(int));
        }

        if (expression is MethodCallExpression compareTo
            && compareTo.Object?.Type == typeof(string)
            && compareTo.Method.Name == nameof(string.CompareTo)
            && compareTo.Arguments.Count == 1)
        {
            var left = VisitScalar(compareTo.Object, scope, typeof(string));
            var right = VisitScalar(compareTo.Arguments[0], scope, typeof(string));
            return new PostgreSqlSqlValue(
                $"(CASE WHEN {left.Sql} < {right.Sql} THEN -1 WHEN {left.Sql} > {right.Sql} THEN 1 ELSE 0 END)",
                typeof(int));
        }

        throw PostgreSqlExpressionUtilities.Unsupported(expression);
    }

    internal string VisitJson(Expression expression, PostgreSqlTranslationScope scope)
    {
        expression = PostgreSqlExpressionUtilities.StripConvert(expression);
        if (expression == scope.Parameter && !scope.HasColumns)
        {
            return scope.JsonExpression;
        }

        if (expression is not MemberExpression member)
        {
            throw PostgreSqlExpressionUtilities.Unsupported(expression);
        }

        return PostgreSqlJsonTranslation.JsonValue(scope.JsonExpression, PostgreSqlExpressionUtilities.GetMemberPath(member, scope.Parameter));
    }
}
