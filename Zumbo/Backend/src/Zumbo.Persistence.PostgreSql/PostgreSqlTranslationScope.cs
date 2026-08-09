using System.Linq.Expressions;

namespace Zumbo.Persistence.PostgreSql;

internal readonly record struct PostgreSqlTranslationScope(ParameterExpression Parameter, string JsonExpression, bool HasColumns);
