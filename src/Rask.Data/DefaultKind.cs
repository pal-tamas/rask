namespace Rask.Data;

/// <summary>What a <see cref="DeclaredDefault" /> says the column defaults to.</summary>
internal enum DefaultKind
{
    /// <summary>A constant, written as the column stores it: <c>Default(x =&gt; x.Size, 1000)</c>.</summary>
    Value,

    /// <summary>A constant already as the column stores it: <c>StoredDefault(x =&gt; x.Number, 0)</c>.</summary>
    Stored,

    /// <summary>The database's clock in UTC, as a <see cref="DateTime" />: <c>() =&gt; Clock.Now.UtcDateTime</c>.</summary>
    NowUtc,

    /// <summary>The database's clock in UTC, as a <see cref="DateTimeOffset" />: <c>() =&gt; Clock.Now</c>.</summary>
    NowOffset,

    /// <summary>The database's wall clock, in the zone its server runs in: <c>() =&gt; Clock.Wall</c>.</summary>
    Wall,
}
