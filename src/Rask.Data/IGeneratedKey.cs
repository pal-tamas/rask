namespace Rask.Data;

/// <summary>An entity whose key the database generates: cleared when the row it was given rolled back (<see cref="AttemptUndo" />).</summary>
internal interface IGeneratedKey
{
    /// <summary>Clears the key, so the next save inserts the row again and is given a new one.</summary>
    void ClearKey();
}
