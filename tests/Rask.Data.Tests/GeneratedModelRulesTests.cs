using Rask.Wire;

namespace Rask.Data.Tests;

/// <summary>The pieces the generated writes and field rules are built from.</summary>
public sealed class GeneratedModelRulesTests
{
    [Fact]
    public void What_the_value_object_says_about_its_empty_value_stands_and_its_silence_becomes_the_one_sentence()
    {
        IEnumerable<string> ownWords = ["A destination needs a name."];

        var kept = GeneratedModelRules.OrRequired(ownWords);
        var accepted = GeneratedModelRules.OrRequired([]);
        var nothing = GeneratedModelRules.OrRequired(null);

        Assert.Equal(["A destination needs a name."], kept);
        Assert.Equal([GeneratedModelRules.RequiredMessage], accepted);
        Assert.Equal([GeneratedModelRules.RequiredMessage], nothing);
    }

    [Fact]
    public void The_one_sentence_is_whatever_the_catalog_gave_for_the_readers_language()
    {
        IEnumerable<string> ownWords = ["A destination needs a name."];

        var alone = GeneratedModelRules.Required("Kötelező kitölteni.");
        var kept = GeneratedModelRules.OrRequired(ownWords, "Kötelező kitölteni.");
        var accepted = GeneratedModelRules.OrRequired([], "Kötelező kitölteni.");

        Assert.Equal(["Kötelező kitölteni."], alone);
        Assert.Equal(["A destination needs a name."], kept);
        Assert.Equal(["Kötelező kitölteni."], accepted);
    }

    [Fact]
    public void A_rule_that_says_nothing_adds_no_failure_and_builds_no_list()
    {
        IList<FieldFailure>? none = null;

        var afterSilence = GeneratedModelRules.Told(none, "", "Name", (IEnumerable<string>)[]);
        var afterNull = GeneratedModelRules.Told(none, "", "Name", (IEnumerable<string>?)null);

        Assert.Null(afterSilence);
        Assert.Null(afterNull);
    }

    [Fact]
    public void Each_message_is_one_failure_under_the_path_a_form_names_the_field_by()
    {
        var root = GeneratedModelRules.Told(null, "", "Name", (IEnumerable<string>)["Too short.", "No digits."]);

        var both = GeneratedModelRules.Told(root, "Lines[2].", "Sku", "Taken twice.");

        Assert.Equal(
            [("Name", "Too short."), ("Name", "No digits."), ("Lines[2].Sku", "Taken twice.")],
            both.Select(f => (f.Fields.Single(), f.Message)));
        Assert.Equal(both, GeneratedModelRules.Refused(both).Failures);
    }
}
