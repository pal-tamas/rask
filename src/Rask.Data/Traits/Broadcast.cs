namespace Rask.Data;

/// <summary>A save of the aggregate is announced to the whole process once it commits, not only to the session that made it.</summary>
public abstract class Broadcast : IAggregateTrait;
