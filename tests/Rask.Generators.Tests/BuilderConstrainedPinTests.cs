namespace Rask.Generators.Tests;

// A component whose type is pinned by more than one step, where one of those type parameters is
// CONSTRAINED and the step that pins it takes a lambda.
//
// Nothing in the repo had that shape until UiDataGrid<T, TKey> made its row key required, and all three
// halves of it were broken in a way no existing test could see:
//
//   - the constraint was written onto the STEP inside a stage rather than onto the stage that declares
//     the type parameter (CS0699), and left off the pending state entirely (CS8714);
//   - a `Func<T, TKey>` could never pin at all, because every delegate was excluded from the pin
//     candidates — a rule that is right for an OPENING and wrong for a step after one;
//   - `Of` was withheld outright from anything with a required step, which left a component whose type
//     no step can pin with no way in.
public class BuilderConstrainedPinTests
{
    // The grid's shape, reduced to what matters: rows pin T, the key selector pins a CONSTRAINED TKey,
    // and the key is required.
    private const string Grid = """
                                using System;
                                using System.Collections.Generic;
                                using Rask.Core;
                                namespace Demo;
                                public partial class Grid<T, TKey> : Component
                                    where TKey : notnull
                                {
                                    public required Func<T, TKey> RowKey { get; set; }
                                    public IEnumerable<T>? Rows { get; set; }
                                    public IReadOnlyList<TKey>? Chosen { get; set; }
                                    public string? Label { get; set; }
                                }
                                """;

    [Fact]
    public void A_stage_carries_the_constraint_for_the_type_parameter_it_declares()
    {
        var output = Entries(Grid);

        // On the STRUCT, which is what declares TKey. Written onto the step instead, it names a type
        // parameter that step does not define and the whole file fails to compile.
        Assert.Contains(
            "readonly struct RaskStage_Grid_Chosen<TKey> where TKey : notnull",
            output, StringComparison.Ordinal);
    }

    [Fact]
    public void The_pending_state_carries_it_too()
    {
        // It names Grid<T, TKey> again, so without the constraint the nullability of its own argument
        // no longer satisfies the component's — CS8714, at a line nobody wrote.
        Assert.Contains(
            "readonly struct RaskPending_Grid<T, TKey> where TKey : notnull",
            Entries(Grid), StringComparison.Ordinal);
    }

    [Fact]
    public void A_lambda_step_pins_the_type_it_returns_once_its_input_is_pinned()
    {
        // `Rows` fixes T, so `RowKey`'s lambda has a parameter type and its body can be bound: TKey comes
        // from the return. This is the step that completes the grid.
        Assert.Contains(
            "RowKey<TKey>(global::System.Func<T, TKey> RowKey) where TKey : notnull",
            Entries(Grid), StringComparison.Ordinal);
    }

    [Fact]
    public void A_lambda_step_does_not_OPEN_a_chain()
    {
        // The other half of the same rule, and the reason every delegate used to be excluded: opening on
        // `RowKey` would leave the lambda's parameter with no type to be, so the call site infers nothing
        // (CS0411). It may complete a chain; it may not start one.
        var seed = Entries(Grid)
            .Split("readonly struct RaskStage", StringSplitOptions.None)[0];

        Assert.DoesNotContain("RowKey<T, TKey>(", seed, StringComparison.Ordinal);
    }

    [Fact]
    public void Of_hands_back_the_state_that_still_owes_the_required_step()
    {
        // Stating the type argument must not be a way PAST a required step. It used to be withheld
        // entirely instead, which is worse: a component whose type no step can pin then has no way in.
        var output = Entries(Grid);

        Assert.Contains("Of<T, TKey>()", output, StringComparison.Ordinal);
        Assert.Contains(
            "global::Demo.RaskPending_Grid<T, TKey> Of<T, TKey>()", output, StringComparison.Ordinal);
    }

    // A carrier is not a delegate, whatever it reads like.
    [Fact]
    public void A_carrier_never_pins_because_no_lambda_can_infer_through_it()
    {
        // Fn<TIn, TOut> is a readonly STRUCT that a lambda reaches by a user-defined conversion, and C#
        // infers no type argument through one of those. Treating it as a delegate produced a way in that
        // was CS0411 at every call site.
        var output = Entries("""
                             using System.Collections.Generic;
                             using Rask.Core;
                             namespace Demo;
                             public partial class Feed<T> : Component
                             {
                                 public Fn<string, T>? Load { get; set; }
                                 public IEnumerable<T>? Rows { get; set; }
                             }
                             """);

        Assert.DoesNotContain("Load<T>(", output, StringComparison.Ordinal);
        Assert.Contains("Rows<T>(", output, StringComparison.Ordinal);
    }

    private static string Entries(string source) =>
        BuilderGeneratorHarness.Run(source).Source("RaskBuilderEntryHost.g.cs");
}
