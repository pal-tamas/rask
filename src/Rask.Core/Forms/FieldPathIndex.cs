using System.Runtime.CompilerServices;
using Rask.Core.Live;

namespace Rask.Core.Forms;

// The paths a form's model reaches its objects by (FieldPath.Index), read once per render pass and shared by every bound
// control of the form that asks: a row of fifty lines asks fifty times, and walks the model once.
internal static class FieldPathIndex
{
    private static readonly ConditionalWeakTable<object, Read> Reads = [];

    /// <summary>The path each object <paramref name="model" /> reaches is named by, as this render pass found them.</summary>
    /// <param name="model">The form's model.</param>
    internal static IReadOnlyDictionary<object, string> Of(object model)
    {
        LiveRenderContext.RenderOwner(out var pass);
        // Outside a render there is no pass to keep a reading for: the model may change before the next ask.
        if (pass == 0)
        {
            return FieldPath.Index(model);
        }

        if (Reads.TryGetValue(model, out var read) && read.Pass == pass)
        {
            return read.Paths;
        }

        read = new Read(pass, FieldPath.Index(model));
        Reads.AddOrUpdate(model, read);
        return read.Paths;
    }

    // A pass is drawn from one process-wide sequence, so a reading is never mistaken for another root's.
    private sealed record Read(long Pass, Dictionary<object, string> Paths);
}
