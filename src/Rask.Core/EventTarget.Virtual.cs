using System.Text.Json;
using Rask.Core.Virtualization;

namespace Rask.Core;

// The runtime's own word on a scroll box, beside MDN's: what it measured of the virtual list the box scrolls
// (rask-input.ts, `raskVirtual`). Internal: nothing a page reads, only what VirtualizeModel windows the rows by.
public sealed partial class EventTarget
{
    /// <summary>What the runtime measured of the virtual list inside this box; null for a box that holds none.</summary>
    internal VirtualReport? Virtual { get; private set; }

    partial void ReadRuntimeState(JsonElement p) => Virtual = VirtualReport.Read(p);
}
