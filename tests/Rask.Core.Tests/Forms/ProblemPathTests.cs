using Rask.Values;

namespace Rask.Core.Tests.Forms;

/// <summary>A problem deep inside a list the value holds is under the path a form shows that row's field at.</summary>
public sealed class ProblemPathTests
{
    private sealed record Pair(string FileId, int DbId);

    private sealed record Well(ValueList<Pair> Companies);

    private sealed record Wells(ValueList<Well> Entries)
    {
        public IEnumerable<Problem> Unknown(int unknown)
        {
            for (var i = 0; i < Entries.Count; i++)
            {
                for (var j = 0; j < Entries[i].Companies.Count; j++)
                {
                    if (Entries[i].Companies[j].DbId == unknown)
                    {
                        yield return Problem.At(() => Entries[i].Companies[j].DbId, "Not this partner's.");
                    }
                }
            }
        }
    }

    [Fact]
    public void A_problem_named_by_a_lambda_is_under_its_evaluated_positions()
    {
        var wells = new Wells([new Well([new Pair("1", 10)]), new Well([new Pair("2", 20), new Pair("3", 99)])]);

        var problem = Assert.Single(wells.Unknown(99));

        Assert.Equal("Entries[1].Companies[1].DbId", problem.Field);
        Assert.Equal("Not this partner's.", problem.Message);
    }

    [Fact]
    public void A_problem_named_by_its_member_keeps_the_member_as_written()
    {
        var quantity = 3;

        var problem = Problem.At(quantity, "Both or neither.");

        Assert.Equal("quantity", problem.Field);
    }
}
