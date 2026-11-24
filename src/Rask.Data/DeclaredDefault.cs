namespace Rask.Data;

/// <summary>
///     A column's default as <c>Default(…)</c> or <c>StoredDefault(…)</c> declared it, carried on the property until
///     <see cref="DatabaseDefaultConvention" /> writes it in the provider's SQL.
/// </summary>
/// <param name="Kind">What it defaults to.</param>
/// <param name="Value">The constant, for <see cref="DefaultKind.Value" /> and <see cref="DefaultKind.Stored" />.</param>
internal sealed record DeclaredDefault(DefaultKind Kind, object? Value)
{
    internal static readonly DeclaredDefault NowUtc = new(DefaultKind.NowUtc, null);

    internal static readonly DeclaredDefault NowOffset = new(DefaultKind.NowOffset, null);

    internal static readonly DeclaredDefault Wall = new(DefaultKind.Wall, null);

    /// <summary>The declaration as the app wrote it, for a refusal to name.</summary>
    internal string Describe(string property) =>
        Kind switch
        {
            DefaultKind.Value => $"Default(x => x.{property}, {Value})",
            DefaultKind.Stored => $"StoredDefault(x => x.{property}, {Value})",
            DefaultKind.Wall => $"Default(x => x.{property}, () => Clock.Wall)",
            DefaultKind.NowUtc => $"Default(x => x.{property}, () => Clock.Now.UtcDateTime)",
            _ => $"Default(x => x.{property}, () => Clock.Now)",
        };
}
