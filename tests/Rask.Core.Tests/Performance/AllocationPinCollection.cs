namespace Rask.Core.Tests.Performance;

/// <summary>
///     The builder allocation pins run on their own, after every parallel class in the assembly has finished.
/// </summary>
/// <remarks>
///     <para>
///         A pin reads <see cref="GC.GetAllocatedBytesForCurrentThread" />, so it looks immune to other tests:
///         their allocations land on their own threads. It is not immune to what they SHARE. The render path
///         borrows its <c>StringBuilder</c>s from <c>RaskStringBuilderPool.Shared</c> — one
///         <c>DefaultObjectPool</c> for every thread in the process — and the head-asset splice borrows two
///         more per render. While another class renders in parallel, this thread finds the pool drained and
///         allocates a fresh builder, or is handed one sized for somebody else's page and grows it. Both are
///         allocations on the measuring thread that the code under test did not cause.
///     </para>
///     <para>
///         That is why <c>An_entry_built_Head_costs_what_it_costs</c> — the probe with a head, and so the
///         heaviest pool user — failed four gates in three days at 1818–1904 B against an 1800 B pin, always
///         on a loaded machine, always green alone and across its own assembly (#1056). Taking the best of
///         three rounds could not fix it: when the contention outlasts the measurement, every round sees it.
///         Not parallelising the class removes the contention rather than averaging it away, and leaves every
///         ceiling where it was.
///     </para>
///     <para>
///         <b>Only <c>BuilderEntryAllocationPinTests</c> is in here, deliberately.</b> The document-shell and
///         counter pins were moved in too, and the document-shell pin — a composed-minus-bare delta — then
///         read 9416 B against its 1280 B ceiling in a full-assembly run, while passing alone and beside every
///         other non-parallel collection. A collection like this runs AFTER the whole parallel phase, so it
///         measures in whatever state the rest of the assembly leaves behind, and something in that state
///         reaches that delta. Neither pin had ever flaked where it was, so they stay there; a pin that joins
///         this collection has to be shown green across several full-assembly runs first.
///     </para>
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AllocationPinCollection
{
    public const string Name = "AllocationPins";
}
