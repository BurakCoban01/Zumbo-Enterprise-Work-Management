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

internal sealed class PostgreSqlExpressionTranslator(JsonSerializerOptions jsonOptions)
{
    private readonly List<NpgsqlParameter> parameters = [];
    private int jsonAliasIndex;

    public IReadOnlyList<NpgsqlParameter> Parameters => parameters;

    public string TranslatePredicate<TDocument>(Expression<Func<TDocument, bool>> expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        return VisitPredicate(expression.Body, new Scope(expression.Parameters[0], "document", HasColumns: true));
    }

    public string TranslateOrder<TDocument>(Expression<Func<TDocument, object>> expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        return VisitScalar(StripConvert(expression.Body), new Scope(expression.Parameters[0], "document", HasColumns: true)).Sql;
    }

    public void AddParameters(NpgsqlCommand command)
    {
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(parameter);
        }
    }

    private string VisitPredicate(Expression expression, Scope scope)
    {
        expression = StripConvert(expression);
        if (!DependsOnParameter(expression))
        {
            return Evaluate(expression) is true ? "TRUE" : "FALSE";
        }

        return expression switch
        {
            BinaryExpression { NodeType: ExpressionType.AndAlso } binary =>
                $"({VisitPredicate(binary.Left, scope)} AND {VisitPredicate(binary.Right, scope)})",
            BinaryExpression { NodeType: ExpressionType.OrElse } binary =>
                $"({VisitPredicate(binary.Left, scope)} OR {VisitPredicate(binary.Right, scope)})",
            BinaryExpression binary when IsComparison(binary.NodeType) => VisitComparison(binary, scope),
            UnaryExpression { NodeType: ExpressionType.Not } unary => $"(NOT {VisitPredicate(unary.Operand, scope)})",
            MethodCallExpression call => VisitBooleanMethod(call, scope),
            MemberExpression member when NonNullable(member.Type) == typeof(bool) =>
                $"({VisitScalar(member, scope).Sql} IS TRUE)",
            ConstantExpression { Value: bool value } => value ? "TRUE" : "FALSE",
            _ => throw Unsupported(expression)
        };
    }

    private string VisitComparison(BinaryExpression expression, Scope scope)
    {
        var leftIsNull = IsNull(expression.Left);
        var rightIsNull = IsNull(expression.Right);
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
            _ => throw Unsupported(expression)
        };
        return $"({left.Sql} {sqlOperator} {right.Sql})";
    }

    private string VisitBooleanMethod(MethodCallExpression call, Scope scope)
    {
        if (IsAny(call))
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
                _ => throw Unsupported(call)
            };
        }

        if (IsContains(call))
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

        throw Unsupported(call);
    }

    private string VisitStringEquals(MethodCallExpression call, Scope scope)
    {
        var source = VisitScalar(call.Object!, scope);
        var argument = VisitScalar(call.Arguments[0], scope, typeof(string));
        if (call.Arguments.Count == 1)
        {
            return $"({source.Sql} = {argument.Sql})";
        }

        var comparison = Evaluate(call.Arguments[1]);
        return comparison switch
        {
            StringComparison.Ordinal => $"({source.Sql} = {argument.Sql})",
            StringComparison.OrdinalIgnoreCase => $"(lower({source.Sql}) = lower({argument.Sql}))",
            _ => throw new DocumentQueryException("Only ordinal string comparison can be translated to PostgreSQL.")
        };
    }

    private string VisitContains(MethodCallExpression call, Scope scope)
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

        if (!DependsOnParameter(sourceExpression))
        {
            if (Evaluate(sourceExpression) is not IEnumerable values)
            {
                throw Unsupported(call);
            }

            var item = VisitScalar(itemExpression, scope);
            var entries = values.Cast<object?>().ToList();
            if (entries.Count == 0)
            {
                return "FALSE";
            }

            var valueParameters = entries.Select(value => AddParameter(value, item.Type));
            return $"({item.Sql} IN ({string.Join(", ", valueParameters)}))";
        }

        var jsonSource = VisitJson(sourceExpression, scope);
        if (DependsOnParameter(itemExpression))
        {
            throw new DocumentQueryException("A document collection can only be searched for a captured scalar value.");
        }

        var itemValue = Evaluate(itemExpression);
        var json = JsonSerializer.Serialize(new[] { itemValue }, jsonOptions);
        var parameter = AddJsonParameter(json);
        return $"(COALESCE({jsonSource}, '[]'::jsonb) @> {parameter}::jsonb)";
    }

    private string VisitAny(MethodCallExpression call, Scope scope)
    {
        var sourceExpression = call.Arguments[0];
        var jsonSource = VisitJson(sourceExpression, scope);
        if (call.Arguments.Count == 1)
        {
            return $"(jsonb_array_length(COALESCE({jsonSource}, '[]'::jsonb)) > 0)";
        }

        var lambda = UnwrapLambda(call.Arguments[1]);
        var alias = $"j{jsonAliasIndex++}";
        var predicate = VisitPredicate(lambda.Body, new Scope(lambda.Parameters[0], $"{alias}.value", HasColumns: false));
        return $"EXISTS (SELECT 1 FROM jsonb_array_elements(COALESCE({jsonSource}, '[]'::jsonb)) AS {alias}(value) WHERE {predicate})";
    }

    private SqlValue VisitScalar(Expression expression, Scope scope, Type? preferredType = null)
    {
        expression = StripConvert(expression);
        if (!DependsOnParameter(expression))
        {
            var value = Evaluate(expression);
            var type = preferredType ?? expression.Type;
            return new SqlValue(AddParameter(value, type), NonNullable(type));
        }

        if (expression == scope.Parameter && !scope.HasColumns)
        {
            return new SqlValue(CastJsonText($"{scope.JsonExpression} #>> '{{}}'", expression.Type), NonNullable(expression.Type));
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
                return new SqlValue($"({nullable.Sql} IS NOT NULL)", typeof(bool));
            }

            var path = GetMemberPath(member, scope.Parameter);
            if (scope.HasColumns && path.Count == 1 && path[0] == nameof(IDocument.Id))
            {
                return new SqlValue("id", typeof(string));
            }

            if (scope.HasColumns && path.Count == 1 && path[0] == nameof(IVersionedDocument.Version))
            {
                return new SqlValue("version", typeof(long));
            }

            var jsonText = JsonText(scope.JsonExpression, path);
            return new SqlValue(CastJsonText(jsonText, member.Type), NonNullable(member.Type));
        }

        if (expression is MethodCallExpression call
            && call.Object is not null
            && call.Arguments.Count == 0
            && call.Method.Name is nameof(string.ToLower) or nameof(string.ToLowerInvariant))
        {
            var operand = VisitScalar(call.Object, scope, typeof(string));
            return new SqlValue($"lower({operand.Sql})", typeof(string));
        }

        if (expression is MethodCallExpression stringCompare
            && stringCompare.Method.DeclaringType == typeof(string)
            && stringCompare.Method.Name is nameof(string.Compare) or nameof(string.CompareOrdinal)
            && stringCompare.Arguments.Count >= 2)
        {
            var left = VisitScalar(stringCompare.Arguments[0], scope, typeof(string));
            var right = VisitScalar(stringCompare.Arguments[1], scope, typeof(string));
            return new SqlValue(
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
            return new SqlValue(
                $"(CASE WHEN {left.Sql} < {right.Sql} THEN -1 WHEN {left.Sql} > {right.Sql} THEN 1 ELSE 0 END)",
                typeof(int));
        }

        throw Unsupported(expression);
    }

    private string VisitJson(Expression expression, Scope scope)
    {
        expression = StripConvert(expression);
        if (expression == scope.Parameter && !scope.HasColumns)
        {
            return scope.JsonExpression;
        }

        if (expression is not MemberExpression member)
        {
            throw Unsupported(expression);
        }

        return JsonValue(scope.JsonExpression, GetMemberPath(member, scope.Parameter));
    }

    private string AddParameter(object? value, Type expectedType)
    {
        var name = $"p{parameters.Count}";
        value = NormalizeParameter(value, NonNullable(expectedType));
        parameters.Add(new NpgsqlParameter(name, value ?? DBNull.Value));
        return $"@{name}";
    }

    private string AddJsonParameter(string json)
    {
        var name = $"p{parameters.Count}";
        parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Jsonb) { Value = json });
        return $"@{name}";
    }

    private static object? NormalizeParameter(object? value, Type type)
    {
        if (value is null)
        {
            return null;
        }

        if (type.IsEnum)
        {
            return Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }

        if (value is DateTime dateTime)
        {
            return dateTime.Kind switch
            {
                DateTimeKind.Utc => dateTime,
                DateTimeKind.Local => dateTime.ToUniversalTime(),
                _ => DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
            };
        }

        if (value is Guid or Uri)
        {
            return value.ToString();
        }

        return value;
    }

    private static string CastJsonText(string sql, Type type)
    {
        type = NonNullable(type);
        if (type == typeof(string) || type == typeof(char) || type == typeof(Guid) || type == typeof(Uri))
        {
            return sql;
        }

        if (type == typeof(DateTime) || type == typeof(DateTimeOffset))
        {
            return $"public.zumbo_parse_timestamptz({sql})";
        }

        var postgresType = type.IsEnum ? "bigint" : Type.GetTypeCode(type) switch
        {
            TypeCode.Boolean => "boolean",
            TypeCode.Byte or TypeCode.SByte or TypeCode.Int16 or TypeCode.UInt16
                or TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 => "bigint",
            TypeCode.UInt64 or TypeCode.Decimal => "numeric",
            TypeCode.Single or TypeCode.Double => "double precision",
            _ => throw new DocumentQueryException($"Member type {type.FullName} cannot be translated to PostgreSQL.")
        };
        return $"({sql})::{postgresType}";
    }

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

    private static string JsonName(MemberInfo member) =>
        member.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? member.Name;

    private static string JsonText(string root, IReadOnlyList<string> path) =>
        $"{root} #>> {PathArray(path)}";

    private static string JsonValue(string root, IReadOnlyList<string> path) =>
        $"{root} #> {PathArray(path)}";

    private static string PathArray(IEnumerable<string> path) =>
        $"ARRAY[{string.Join(", ", path.Select(segment => $"'{segment.Replace("'", "''", StringComparison.Ordinal)}'"))}]";

    private static LambdaExpression UnwrapLambda(Expression expression)
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

    private static bool IsAny(MethodCallExpression call) =>
        call.Method.Name == nameof(Enumerable.Any)
        && call.Method.DeclaringType == typeof(Enumerable)
        && call.Arguments.Count is 1 or 2;

    private static bool IsContains(MethodCallExpression call) =>
        call.Method.Name == nameof(Enumerable.Contains)
        || (call.Method.Name == nameof(IList.Contains) && call.Object?.Type != typeof(string));

    private static bool IsComparison(ExpressionType type) =>
        type is ExpressionType.Equal or ExpressionType.NotEqual
            or ExpressionType.GreaterThan or ExpressionType.GreaterThanOrEqual
            or ExpressionType.LessThan or ExpressionType.LessThanOrEqual;

    private static bool IsNull(Expression expression) =>
        !DependsOnParameter(expression) && Evaluate(expression) is null;

    private static Type NonNullable(Type type) => Nullable.GetUnderlyingType(type) ?? type;

    private static Expression StripConvert(Expression expression)
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

    private static object? Evaluate(Expression expression)
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

    private static bool DependsOnParameter(Expression expression)
    {
        var visitor = new ParameterFindingVisitor();
        visitor.Visit(expression);
        return visitor.Found;
    }

    private static DocumentQueryException Unsupported(Expression expression) =>
        new($"Expression '{expression}' ({expression.NodeType}) is not supported by PostgreSQL persistence.");

    private readonly record struct Scope(ParameterExpression Parameter, string JsonExpression, bool HasColumns);
    private readonly record struct SqlValue(string Sql, Type Type);

    private sealed class ParameterFindingVisitor : ExpressionVisitor
    {
        public bool Found { get; private set; }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            Found = true;
            return node;
        }
    }
}
