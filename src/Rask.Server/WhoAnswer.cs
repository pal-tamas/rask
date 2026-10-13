namespace Rask.Server;

/// <summary>When a session's next event asks who its user is.</summary>
internal enum WhoAnswer
{
    /// <summary>Every event asks.</summary>
    PerEvent,

    /// <summary>A batch is being dispatched and none of its events has asked yet.</summary>
    OwedToBatch,

    /// <summary>An event of the batch being dispatched has asked, and the rest go by its answer.</summary>
    GivenToBatch,
}
