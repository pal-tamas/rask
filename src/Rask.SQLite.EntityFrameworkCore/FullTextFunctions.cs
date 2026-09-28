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

/// <summary>
/// The SQL functions the query rewrite leaves behind, translated to FTS5's auxiliary functions. Never executed.
/// </summary>
internal static class FullTextFunctions
{
    public static readonly MethodInfo HighlightMethod = typeof(FullTextFunctions).GetMethod(nameof(Highlight))!;

    public static readonly MethodInfo SnippetMethod = typeof(FullTextFunctions).GetMethod(nameof(Snippet))!;

    public static string? Highlight(string? index, int column, string open, string close) =>
        throw FullText.OutsideQuery(nameof(FullText.Highlight));

    public static string? Snippet(string? index, int column, string open, string close, string ellipsis, int tokens) =>
        throw FullText.OutsideQuery(nameof(FullText.Snippet));
}
