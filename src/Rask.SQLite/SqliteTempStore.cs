namespace Rask.SQLite;

/// <summary>The SQLite <c>temp_store</c> setting — where temporary tables and indices live.</summary>
public enum SqliteTempStore
{
    /// <summary>Use the compile-time default (usually a file).</summary>
    Default,

    /// <summary>Store temporary objects in a file.</summary>
    File,

    /// <summary>Store temporary objects in memory.</summary>
    Memory,
}
