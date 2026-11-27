namespace Rask.Generators.Shared;

/// <summary>What a typed id holds: the one value its <c>Value</c> parameter declares.</summary>
internal enum TypedIdValue
{
    /// <summary>Not a typed id.</summary>
    None,

    /// <summary>An <c>int</c> — an identity column's number.</summary>
    Int32,

    /// <summary>A <c>long</c> — an identity column's number.</summary>
    Int64,

    /// <summary>A <c>Guid</c> — the application's own, from <c>Create()</c>.</summary>
    Guid,

    /// <summary>A <c>string</c> — a natural key.</summary>
    String,
}
