namespace Rask.Querying;

/// <summary>What an entry held before a command touched it.</summary>
internal interface IOptimisticSnapshot
{
    void Restore();
}
