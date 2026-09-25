using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rask.Data;

namespace Rask.SQLite;

/// <summary>Translates <see cref="FullTextFunctions"/> to <c>highlight(...)</c> and <c>snippet(...)</c>.</summary>
internal sealed class FullTextFunctionTranslatorPlugin(ISqlExpressionFactory sql) : IMethodCallTranslatorPlugin
{
    public IEnumerable<IMethodCallTranslator> Translators { get; } = [new Translator(sql)];

    private sealed class Translator(ISqlExpressionFactory sql) : IMethodCallTranslator
    {
        public SqlExpression? Translate(
            SqlExpression? instance,
            MethodInfo method,
            IReadOnlyList<SqlExpression> arguments,
            IDiagnosticsLogger<DbLoggerCategory.Query> logger)
        {
            string? name = null;
            if (method == FullTextFunctions.HighlightMethod)
            {
                name = "highlight";
            }
            else if (method == FullTextFunctions.SnippetMethod)
            {
                name = "snippet";
            }

            // The first argument is FTS5's hidden table column, which the auxiliary function reads as a handle to the
            // current match rather than as a value — so it is passed as the column, untouched.
            return name is null
                ? null
                : sql.Function(
                    name,
                    arguments,
                    nullable: true,
                    argumentsPropagateNullability: arguments.Select(static _ => false),
                    typeof(string));
        }
    }
}
