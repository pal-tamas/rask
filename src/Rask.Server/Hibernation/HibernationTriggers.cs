namespace Rask.Server.Hibernation;

/// <summary>The words a trigger goes by on the <c>rask.sessions.hibernated</c> metric.</summary>
internal static class HibernationTriggers
{
    public static string Tag(HibernationTrigger trigger) => trigger == HibernationTrigger.Pressure ? "pressure" : "hidden";
}
