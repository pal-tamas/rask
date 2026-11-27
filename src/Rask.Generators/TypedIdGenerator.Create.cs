using System.Text;

namespace Rask.Generators;

public sealed partial class TypedIdGenerator
{
    // Create() of an id over a Guid: Rask.Uuid.Create(), the one monotonic version-7 maker (in Rask.Wire, which every
    // project this generator runs in references through the Rask package). Guid.CreateVersion7() alone fills rand_a at
    // random, so two ids in one millisecond would come out in either order.
    private static void AppendCreate(StringBuilder sb, string name)
    {
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    ///     A new id: a version-7 <see cref=\"global::System.Guid\" />, time-ordered and strictly increasing within");
        sb.AppendLine("    ///     this process (<see cref=\"global::Rask.Uuid.Create()\" />), so the key's index stays append-only.");
        sb.AppendLine("    /// </summary>");
        sb.Append("    public static ").Append(name).AppendLine(" Create() => new(global::Rask.Uuid.Create());");
        sb.AppendLine();
    }
}
