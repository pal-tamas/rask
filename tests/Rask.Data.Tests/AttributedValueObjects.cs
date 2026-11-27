using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

/// <summary>Value objects whose columns an attribute on the property shapes instead of their own declaration.</summary>
public sealed class Granary : Aggregate<int>, IHasForm
{
    private Granary() { }

#pragma warning disable RASK128 // the attribute disagrees with RigDriver's MaxLength: 20 on purpose — it is what is tested
    [MaxLength(50)]
    public RigDriver Keeper { get; private set; } = null!;

    [StringLength(10)]
    public RigDriver Deputy { get; private set; } = null!;

    [Precision(12, 3)]
    public RigLitres Stock { get; private set; } = null!;
#pragma warning restore RASK128

    [Required]
    public RigChip? Badge { get; private set; }
}
