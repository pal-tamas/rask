using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NpgsqlTypes;
using Rask.Data;

namespace Rask.Postgres;

/// <summary>
/// <c>HasFullTextSearch</c>, <c>Search(text)</c> and <c>FullText.Highlight</c>/<c>Snippet</c> on PostgreSQL (#1109).
/// </summary>
/// <remarks>
/// <para>
/// PostgreSQL has full-text search built in, and Npgsql maps it, so this is mostly wiring rather than SQL. Each
/// entity that declares an index gets a stored generated <c>tsvector</c> column over the declared properties
/// (<c>IsGeneratedTsVectorColumn</c>) and a GIN index on it — no triggers, because the database keeps a generated
/// column current itself. <c>Search(text)</c> becomes <c>vector @@ to_tsquery(config, 'w1' &amp; 'w2':*)</c> ranked by
/// <c>ts_rank_cd</c>, and the highlight markers become <c>ts_headline</c> with Rask's U+E000/U+E001 around each match,
/// so <c>UiHighlight</c> renders it unchanged.
/// </para>
/// <para>
/// The tokenizers map to text search configurations: <see cref="FullTextTokenizer.English" /> to PostgreSQL's
/// <c>english</c> (stemming, like FTS5's porter), <see cref="FullTextTokenizer.Unicode" /> to <c>rask_unicode</c> —
/// <c>simple</c> with <c>unaccent</c> in front of it, so <c>keres</c> finds <c>kérés</c> as it does on SQLite.
/// <c>unaccent()</c> itself is not IMMUTABLE and cannot sit in a generated column; a configuration that calls it as a
/// dictionary can, which is why it is a configuration, created by the migration that first needs it.
/// </para>
/// </remarks>
internal static class PostgresFullTextSearch
{
    /// <summary>The shadow property, and column, holding each searchable row's <c>tsvector</c>.</summary>
    public const string VectorProperty = "RaskSearchVector";

    public const string UnicodeConfiguration = "rask_unicode";

    public static string ConfigurationFor(FullTextSearchSpec spec) =>
        spec.Tokenizer == FullTextTokenizer.English ? "english" : UnicodeConfiguration;

    /// <summary>
    /// Creates <see cref="UnicodeConfiguration" /> if it is not there. Idempotent, so any migration that builds a
    /// Unicode index can run it; the extension is created in the database's default schema, the configuration in the
    /// migration's current one, which is where <c>to_tsvector('rask_unicode', …)</c> will look for it.
    /// </summary>
    public const string CreateUnicodeConfiguration = """
        CREATE EXTENSION IF NOT EXISTS unaccent;
        DO $rask$
        BEGIN
            IF NOT EXISTS (
                SELECT 1 FROM pg_ts_config WHERE cfgname = 'rask_unicode' AND pg_ts_config_is_visible(oid)) THEN
                CREATE TEXT SEARCH CONFIGURATION rask_unicode (COPY = simple);
                ALTER TEXT SEARCH CONFIGURATION rask_unicode
                    ALTER MAPPING FOR hword, hword_part, word WITH unaccent, simple;
            END IF;
        END
        $rask$;
        """;
}
