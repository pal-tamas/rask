namespace Rask.Querying;

/// <summary>A handle a render slot can set aside when a render stops using it.</summary>
internal interface IRenderSlotHandle
{
    /// <summary>Stops watching, unless it was read during <paramref name="generation" />.</summary>
    void ReleaseUnlessReadIn(long generation);
}
